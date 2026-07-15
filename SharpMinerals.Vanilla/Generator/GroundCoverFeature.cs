using SharpMinerals.Blocks;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Features;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Ground cover - short grass and flowers on exposed grass, dead bushes on exposed sand / red sand. One
/// cell placed just above the surface, so it is a per-column feature (never crosses a border) that reads the
/// finished cube; run after trees, it naturally skips columns capped by a trunk or canopy. The surface block it
/// lands on picks the plant, and each roll folds in the biome's densities (flowers and dead bushes also the
/// feature-density map, forming patches).</summary>
public sealed class GroundCoverFeature : Feature {
    const int SaltFlower = 0xF1, SaltFlowerPick = 0xF2, SaltGrass = 0x67, SaltDeadBush = 0xDB;

    readonly BlockType grassBlock = VanillaMod.GrassBlock;
    readonly BlockType redSand = VanillaMod.RedSand;
    readonly BlockType sand = VanillaMod.Sand;
    readonly BlockType shortGrass = VanillaMod.ShortGrass;
    readonly BlockType deadBush = VanillaMod.DeadBush;
    readonly BlockType[] flowers = {
        VanillaMod.Dandelion, VanillaMod.Poppy, VanillaMod.Cornflower, VanillaMod.OxeyeDaisy,
    };

    public override Extent Extent => Extent.Cover;

    public override void Place(in PlaceContext ctx, CubeSink sink) {
        var surface = ctx.Anchor.Surface;
        int wx = ctx.X, wz = ctx.Z, aboveY = ctx.Anchor.Y + 1;
        var biome = ctx.Biome;
        var rng = ctx.Rng;

        if (surface == grassBlock) {
            if (biome.FlowerDensity > 0.0 && rng.Chance(SaltFlower, biome.FlowerDensity * ctx.Source.FeatureDensity(wx, wz)))
                sink.PlaceIfAir(wx, aboveY, wz, flowers[rng.Range(SaltFlowerPick, flowers.Length)]);
            else if (biome.GrassDensity > 0.0 && rng.Chance(SaltGrass, biome.GrassDensity))
                sink.PlaceIfAir(wx, aboveY, wz, shortGrass);
        } else if (surface == redSand || surface == sand) {
            if (biome.DeadBushDensity > 0.0 && rng.Chance(SaltDeadBush, biome.DeadBushDensity * ctx.Source.FeatureDensity(wx, wz)))
                sink.PlaceIfAir(wx, aboveY, wz, deadBush);
        }
    }
}
