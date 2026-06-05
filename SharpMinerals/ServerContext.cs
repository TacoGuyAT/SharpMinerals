using System.Collections.Concurrent;
using SharpMinerals.Level;
using SharpMinerals.Network;
using SharpMinerals.Persistence;

namespace SharpMinerals;

/// <summary>
/// Immutable wiring handed to a <see cref="Server"/> at construction: the network
/// transport, the set of worlds, and presentation settings. Keeping this separate
/// from <see cref="Server"/> makes the server testable with a fake transport.
/// </summary>
public struct ServerContext {
    public INetServer NetServer;
    public ConcurrentDictionary<string, World> Worlds;
    public string MOTD;
    public int MaxPlayers;
    public double TicksPerSecond;

    /// <summary>
    /// Backend for cross-session entity persistence (players today). Null => the server uses an in-memory store
    /// (survives reconnects, not restarts); a host can supply a disk-backed one (RocksDB).
    /// </summary>
    public IEntityStore? EntityStore;
}
