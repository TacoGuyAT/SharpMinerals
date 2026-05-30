using SharpMinerals.Entities;
using SharpMinerals.Math;
using System.Collections.Concurrent;

namespace SharpMinerals;
public class World : ITickable {
    IChunkGenerator chunkGenerator;
    ConcurrentDictionary<Vector2i, Chunk> loadedChunks = new();
    bool isActive = false;
    public World(IChunkGenerator chunkGenerator) {
        this.chunkGenerator = chunkGenerator;
    }
    public void Tick() {
        if(!isActive) return;
        loadedChunks.Values.AsParallel().ForAll(c => c.Tick());
    }
    public void Spawn(Entity entity) {

    }
}
