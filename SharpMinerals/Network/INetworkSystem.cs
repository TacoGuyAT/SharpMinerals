namespace SharpMinerals.Network;

/// <summary>A world system that also projects its own results to clients. <see cref="Announce"/> runs before
/// the world tick (newly-spawned entities, at their un-decayed spawn state); <see cref="Flush"/> runs after
/// (results + despawns). Both are optional — a system overrides only the phase it needs. The server discovers
/// these on each world's system list, so a world that doesn't run the system sends nothing.</summary>
public interface INetworkSystem {
    void Announce(Server server) { }
    void Flush(Server server) { }
}
