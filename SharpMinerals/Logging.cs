using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpMinerals;

/// <summary>Logging seam for the core library. Depends only on the Microsoft.Extensions.Logging
/// abstractions; the concrete backend is the host's concern. A host assigns <see cref="Factory"/> once at
/// startup — until it does, logging is a no-op (the right default for an embeddable library).</summary>
public static class Logging {
    /// <summary>The host-provided logger factory. Defaults to a no-op; a host sets it at startup.</summary>
    public static ILoggerFactory Factory { get; set; } = NullLoggerFactory.Instance;

    public static ILogger<T> For<T>() => Factory.CreateLogger<T>();
    public static ILogger For(string category) => Factory.CreateLogger(category);
}
