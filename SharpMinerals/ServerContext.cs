using SharpMinerals.Network;
using System.Collections.Concurrent;

namespace SharpMinerals;

public struct ServerContext {
    public INetServer NetServer;
    public ConcurrentDictionary<string, World> Worlds;
    public string MOTD;
}