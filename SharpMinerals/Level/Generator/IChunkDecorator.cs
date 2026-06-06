using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator;

/// <summary>A post-generation pass that stamps structures (trees, ore veins, ...) into a finished cube, after
/// the per-cell shaders have laid down terrain. Unlike a shader, a decorator writes whole multi-cell features,
/// so it runs once per cube with write access to the chunk. Placement must be deterministic and stateless: a
/// feature whose origin sits in a neighbouring cube is stamped (only its overlapping cells) by every cube it
/// reaches into, all deriving the same origin from the world seed, so it stitches across borders without any
/// shared state.</summary>
public interface IChunkDecorator {
    void Decorate(Chunk chunk, Vector3i cube);
}
