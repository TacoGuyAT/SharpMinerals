using SharpMinerals.Entities;
using SharpMinerals.Network;
using System.Collections.Concurrent;

namespace SharpMinerals;

public class Server : ITickable {
    static Server? instance;
    public static Server? Instance => instance;
    public static bool IsRunning => Instance == null;
    ServerContext serverContext;
    public INetServer NetServer => serverContext.NetServer;
    public ConcurrentDictionary<string, World> Worlds => serverContext.Worlds;
    public string MOTD { get => serverContext.MOTD; set => serverContext.MOTD = value; }
    public Server(ServerContext ctx) {
        instance = this;
        serverContext = ctx;
    }
    ~Server() {
        instance = null;
    }
    public void Start() {

    }
    public void Tick() {
        Worlds.Values.AsParallel().ForAll(w => w.Tick());
    }
    public void AddPlayer(NetClient client) {
        var player = new Player();
        Worlds.First().Value.Spawn(player);
    }
}
