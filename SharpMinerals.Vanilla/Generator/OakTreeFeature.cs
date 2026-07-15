using SharpMinerals.Blocks;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Features;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>A simple oak: a 4-6 block trunk under a small cut-corner canopy. Scattered on a jittered 6-grid over
/// dry, non-coastal ground in wooded biomes (see the placement wired in <see cref="OverworldChunkGenerator"/>).
/// The feature is purely the shape; where it roots and how densely is the <see cref="Feature.Scatter"/> chain.</summary>
public sealed class OakTreeFeature : Feature {
    const int SaltTrunk = 0x1B; // this feature's own roll; distinct from the scatter driver's reserved salts

    readonly BlockType log = VanillaMod.OakLog;
    readonly BlockType leaves = VanillaMod.OakLeaves;

    // Radius 2 = canopy reach; Up 9 = tallest trunk + canopy, used for scatter reach and cube culling.
    public override Extent Extent => new(Radius: 2, Up: 9, Down: 0);

    public override void Place(in PlaceContext ctx, CubeSink sink) {
        int ox = ctx.X, oz = ctx.Z, treeBase = ctx.Anchor.Y + 1;
        int trunkH = 4 + ctx.Rng.Range(SaltTrunk, 3); // 4..6 logs

        // Trunk first, so a leaf never overwrites a log where the canopy passes the trunk column.
        for (int i = 0; i < trunkH; i++)
            sink.PlaceIfAir(ox, treeBase + i, oz, log);

        int canopyBase = treeBase + trunkH - 2;
        LeafLayer(sink, ox, canopyBase, oz, radius: 2, cutCorners: true);
        LeafLayer(sink, ox, canopyBase + 1, oz, radius: 2, cutCorners: true);
        LeafLayer(sink, ox, canopyBase + 2, oz, radius: 1, cutCorners: false);
        LeafLayer(sink, ox, canopyBase + 3, oz, radius: 1, cutCorners: true);
    }

    void LeafLayer(CubeSink sink, int ox, int y, int oz, int radius, bool cutCorners) {
        for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++) {
                if (cutCorners && System.Math.Abs(dx) == radius && System.Math.Abs(dz) == radius) continue;
                sink.PlaceIfAir(ox + dx, y, oz + dz, leaves);
            }
    }
}
