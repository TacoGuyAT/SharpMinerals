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
    /// is the configured minimum. The interactive console sink is routed through <paramref name="renderer"/>.
    /// Call once at startup before constructing the server.
    /// </summary>
    public static void Configure(ConsoleRenderer renderer, string? logsDir, LogLevel level) {
        bool toFile = !string.IsNullOrWhiteSpace(logsDir);
        if (toFile) Directory.CreateDirectory(logsDir!);

        Logging.Factory = LoggerFactory.Create(builder => {
            builder.SetMinimumLevel(ToMs(level));

            // Console gets the coloured layout (plain when stdout is redirected); the file gets the plain one.
            // On an interactive TTY, route the console sink through the renderer so log lines repaint around the
            // input prompt instead of corrupting it. When redirected (the piped `tail | server` harness, a file, CI),
            // there's no live prompt to protect: keep the standard console sink byte-for-byte unchanged.
            if (renderer.IsInteractive)
                builder.AddZLoggerStream(renderer.LogStream, options => options.UsePlainTextFormatter(FormatColoured));
            else
                builder.AddZLoggerConsole(options => options.UsePlainTextFormatter(FormatColoured));

            if (toFile) {
                // One file per run: the start time is stamped into the name once, so a restart never appends to
                // a prior run's log. A long run still rolls to a _NNN sequence suffix past the size cap.
                string runStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                builder.AddZLoggerRollingFile(options => {
                    options.FilePathSelector = (_, sequence) =>
                        Path.Combine(logsDir!, $"sharpminerals-{runStamp}_{sequence:000}.log");
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
                template.Format(info.Timestamp, AbbreviateColored(info.LogLevel), info.Category));

    static string Abbreviate(MsLogLevel level) => level switch {
        MsLogLevel.Trace => "TRC",
        MsLogLevel.Debug => "DBG",
        MsLogLevel.Information => "INF",
        MsLogLevel.Warning => "WRN",
        MsLogLevel.Error => "ERR",
        MsLogLevel.Critical => "CRT",
        _ => "OFF",
    };

    static string AbbreviateColored(MsLogLevel level) => level switch {
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
            var span = scratch.WrittenSpan;
            // Most lines carry no colour; the vectorized ESC scan is far cheaper than the byte-wise CSI walk,
            // so skip the latter entirely (and copy in one shot) unless an escape is actually present.
            if (span.IndexOf((byte)0x1b) < 0) writer.Write(span);
            else StripCsi(span, writer);
        }

        // Copy src to dst, dropping CSI sequences: ESC '[' , parameter/intermediate bytes (0x20-0x3F),
        // then one final byte (0x40-0x7E). A lone ESC (not followed by '[') is left as-is.
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
