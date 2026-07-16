using SharpMinerals.Chat;
using SharpMinerals.Level;
using SharpMinerals.Persistence;
using System.Collections.Concurrent;

namespace SharpMinerals;

/// <summary>
/// Immutable wiring handed to a <see cref="Server"/> at construction: the network
/// transport, the set of worlds, and presentation settings. Keeping this separate
/// from <see cref="Server"/> makes the server testable with a fake transport.
/// </summary>
public struct ServerContext {
    public ConcurrentDictionary<string, World> Worlds;
    /// <summary>Creates the store a world created at runtime should own (the same backend the host gave the
    /// startup worlds). Null = a host without persistence; runtime worlds are then in-memory.</summary>
    public Func<string, IWorldStore>? WorldStoreFactory;
    public ChatComponent MOTD;
    public int MaxPlayers;
    public double TicksPerSecond;
}
