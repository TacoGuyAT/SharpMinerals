using SharpMinerals.Chat;
using SharpMinerals.Level;
using System.Collections.Concurrent;

namespace SharpMinerals;

/// <summary>
/// Immutable wiring handed to a <see cref="Server"/> at construction: the network
/// transport, the set of worlds, and presentation settings. Keeping this separate
/// from <see cref="Server"/> makes the server testable with a fake transport.
/// </summary>
public struct ServerContext {
    public ConcurrentDictionary<string, World> Worlds;
    public ChatComponent MOTD;
    public int MaxPlayers;
    public double TicksPerSecond;
}
