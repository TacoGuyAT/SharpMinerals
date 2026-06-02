using System.Buffers;
using Microsoft.Extensions.Logging;
using Kokuban;
using ZLogger;
using ZLogger.Formatters;
using ZLogger.Providers;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace SharpMinerals.CLI;

/// <summary>
/// Host-side logging bootstrap: builds the ZLogger backend (console plus an optional daily rolling file) and
/// installs it as the core library's <see cref="Logging.Factory"/>, so core carries no logging-impl dependency.
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

            // Console gets the coloured layout (plain when stdout is redirected); the file gets the plain one.
            builder.AddZLoggerConsole(options => options.UsePlainTextFormatter(FormatColoured));

            if (toFile) {
                // Daily rolling file with a sequence suffix once a day's log exceeds the size cap.
                builder.AddZLoggerRollingFile(options => {
                    options.FilePathSelector = (date, sequence) =>
                        Path.Combine(logsDir!, $"sharpminerals-{date.ToLocalTime():yyyy-MM-dd}_{sequence:000}.log");
                    options.RollingInterval = RollingInterval.Day;
                    options.RollingSizeKB = 1024;
                    // A coloured chat line shares its ANSI message body with the console sink; strip escape
                    // codes here so the file is always clean regardless of the console's colour state.
                    options.UseFormatter(() => {
                        var inner = new PlainTextZLoggerFormatter();
                        FormatPlain(inner);
                        return new AnsiStrippingFormatter(inner);
                    });
                });
            }
        });

        // ZLogger buffers on a background thread and flushes only on dispose, so dispose the factory at process
        // exit or the tail of the log (shutdown messages, a final exception) is lost.
        var factory = Logging.Factory;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => factory.Dispose();
    }

    // [HH:mm:ss.fff LVL] Category: message prefix. FormatPlain is the file form; FormatColoured is the console
    // form (Abbreviate colours the level token by severity).
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

    // Three-letter upper-case level token, coloured by severity.
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
    /// Wraps a formatter and strips ANSI CSI escape sequences from its output, so the file sink never lands
    /// raw escape codes on disk. Formatting runs on a single background thread, so the scratch buffer is reused.
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
