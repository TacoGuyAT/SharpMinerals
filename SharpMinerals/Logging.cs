using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace SharpMinerals;

/// <summary>
/// Central logging bootstrap. Serilog is the backend; the rest of the codebase
/// talks to it through Microsoft.Extensions.Logging's <see cref="ILogger"/>
/// abstraction. Packet send/receive is traced here at Debug level, which is what
/// surfaces protocol desyncs (wrong field/packet shapes) during development.
/// </summary>
public static class Logging {
    public static ILoggerFactory Factory { get; }

    static Logging() {
        var minimum = Environment.GetEnvironmentVariable("SHARPMINERALS_LOG") switch {
            "trace" or "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "warn" => LogEventLevel.Warning,
            _ => LogEventLevel.Information,
        };

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(minimum)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: true));
    }

    public static ILogger<T> For<T>() => Factory.CreateLogger<T>();
    public static Microsoft.Extensions.Logging.ILogger For(string category) => Factory.CreateLogger(category);
}
