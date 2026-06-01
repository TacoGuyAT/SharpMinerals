using System.Buffers;
using Microsoft.Extensions.Logging;
using Kokuban;
using ZLogger;
using ZLogger.Formatters;
using ZLogger.Providers;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace SharpMinerals.CLI;

/// <summary>
/// Host-side logging bootstrap. Builds the ZLogger backend (console plus an optional daily rolling
/// file) and installs it as the core library's <see cref="Logging.Factory"/>. Keeping this in the
/// executable is what lets the core library carry no logging-implementation dependency.
/// </summary>
static class LoggingSetup {
    /// <summary>
    /// Configures logging and installs it on <see cref="Logging.Factory"/>: console plus a daily
    /// rolling file in <paramref name="logsDir"/> (null/empty = console only). <paramref name="level"/>
    /// is the configured minimum. Call once at startup before constructing the server.
    /// </summary>
    public static void Configure(string? logsDir, LogLevel level) {
        bool toFile = !string.IsNullOrWhiteSpace(logsDir);
        if (toFile) Directory.CreateDirectory(logsDir!);

        Logging.Factory = LoggerFactory.Create(builder => {
            builder.SetMinimumLevel(ToMs(level));

            // Console gets the colour-coded layout (Kokuban emits ANSI only on a colour-capable terminal,
            // and yields plain text when stdout is redirected); the file always gets the plain layout.
            builder.AddZLoggerConsole(options => options.UsePlainTextFormatter(FormatColoured));

            if (toFile) {
                // Daily rolling file: sharpminerals-yyyy-MM-dd_000.log, with a sequence suffix if a single
                // day's log exceeds the size cap. Mirrors the old Serilog RollingInterval.Day sink.
                builder.AddZLoggerRollingFile(options => {
                    options.FilePathSelector = (date, sequence) =>
                        Path.Combine(logsDir!, $"sharpminerals-{date.ToLocalTime():yyyy-MM-dd}_{sequence:000}.log");
                    options.RollingInterval = RollingInterval.Day;
                    options.RollingSizeKB = 1024;
                    // The prefix is plain, but a coloured chat line (rendered by ChatAnsi on a terminal) carries
                    // ANSI in the message body, and that body string is shared with the console sink. Strip any
                    // escape codes here so the file is always clean, regardless of the console's colour state.
                    options.UseFormatter(() => {
                        var inner = new PlainTextZLoggerFormatter();
                        FormatPlain(inner);
                        return new AnsiStrippingFormatter(inner);
                    });
                });
            }
        });

        // ZLogger writes on a background thread, so buffered entries are flushed only on dispose — unlike
        // Serilog's synchronous sinks. Dispose the factory at process exit so the tail of the log (shutdown
        // messages, a final exception) isn't lost. Idempotent, and keeps flush ownership here in the setup.
        var factory = Logging.Factory;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => factory.Dispose();
    }

    // [HH:mm:ss.fff LVL] Category: message{newline}{exception} — the layout the old Serilog template emitted.
    // FormatPlain is the file/redirected form; FormatColoured (console) colours the level token by severity,
    // the way Serilog's literate console theme did.
    static void FormatPlain(PlainTextZLoggerFormatter formatter) =>
        formatter.SetPrefixFormatter(
            $"[{0:HH:mm:ss.fff} {1}] {2}: ",
            (in MessageTemplate template, in LogInfo info) =>
                template.Format(info.Timestamp, Abbreviate(info.LogLevel), info.Category));

    static void FormatColoured(PlainTextZLoggerFormatter formatter) =>
        formatter.SetPrefixFormatter(
            $"[{0:HH:mm:ss.fff} {1}] {2}: ",
            (in MessageTemplate template, in LogInfo info) =>
                template.Format(info.Timestamp, Abbreviate(info.LogLevel), info.Category));

    // Serilog's "u3" three-letter, upper-case level abbreviation.
    static string Abbreviate(MsLogLevel level) => level switch {
        MsLogLevel.Trace => Chalk.Gray["TRC"],
        MsLogLevel.Debug => Chalk.Cyan["DBG"],
        MsLogLevel.Information => Chalk.BrightBlue["INF"],
        MsLogLevel.Warning => Chalk.Yellow["WRN"],
        MsLogLevel.Error => Chalk.Red["ERR"],
        MsLogLevel.Critical => Chalk.BrightWhite.BgRed["CRT"],
        _ => Chalk.White["OFF"],
    };

    static MsLogLevel ToMs(LogLevel level) => level switch {
        LogLevel.Trace => MsLogLevel.Trace,
        LogLevel.Debug => MsLogLevel.Debug,
        LogLevel.Info => MsLogLevel.Information,
        LogLevel.Warn => MsLogLevel.Warning,
        LogLevel.Error => MsLogLevel.Error,
        _ => MsLogLevel.Information,
    };

    /// <summary>
    /// Wraps another formatter and removes ANSI CSI escape sequences (e.g. SGR colour codes) from its output
    /// before writing. Used for the file sink so a colour line that's fine on the console never lands as raw
    /// escape codes on disk. A provider formats on a single background thread, so the scratch buffer is reused.
    /// </summary>
    sealed class AnsiStrippingFormatter(IZLoggerFormatter inner) : IZLoggerFormatter {
        readonly ArrayBufferWriter<byte> scratch = new();

        public bool WithLineBreak => inner.WithLineBreak;

        public void FormatLogEntry(IBufferWriter<byte> writer, IZLoggerEntry entry) {
            scratch.Clear();
            inner.FormatLogEntry(scratch, entry);
            StripCsi(scratch.WrittenSpan, writer);
        }

        // Copy src to dst, dropping CSI sequences: ESC '[' , parameter/intermediate bytes (0x20–0x3F),
        // then one final byte (0x40–0x7E). A lone ESC (not followed by '[') is left as-is.
        static void StripCsi(ReadOnlySpan<byte> src, IBufferWriter<byte> dst) {
            int i = 0, runStart = 0;
            while (i < src.Length) {
                if (src[i] == 0x1b && i + 1 < src.Length && src[i + 1] == (byte)'[') {
                    if (i > runStart) dst.Write(src[runStart..i]);
                    i += 2;
                    while (i < src.Length && src[i] is >= 0x20 and <= 0x3f) i++;
                    if (i < src.Length && src[i] is >= 0x40 and <= 0x7e) i++;
                    runStart = i;
                } else {
                    i++;
                }
            }
            if (i > runStart) dst.Write(src[runStart..i]);
        }
    }
}
