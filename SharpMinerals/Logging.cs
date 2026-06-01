using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpMinerals;

/// <summary>
/// Logging seam for the core library. The library depends only on the Microsoft.Extensions.Logging
/// ABSTRACTIONS — the concrete backend (ZLogger, console/file sinks, level selection) is the HOST's
/// concern: a host assigns <see cref="Factory"/> once at startup, before constructing the server
/// (see SharpMinerals.CLI's <c>LoggingSetup</c>). Until it does, logging is a no-op — the right,
/// non-intrusive default for an embeddable library. Packet send/receive is traced at Debug.
/// </summary>
public static class Logging {
    /// <summary>
    /// The host-provided logger factory. Defaults to a no-op; a host sets it at startup so loggers
    /// created by core types bind to the configured backend.
    /// </summary>
    public static ILoggerFactory Factory { get; set; } = NullLoggerFactory.Instance;

    public static ILogger<T> For<T>() => Factory.CreateLogger<T>();
    public static ILogger For(string category) => Factory.CreateLogger(category);
}
