using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Items;
using SharpMinerals.Modding;

namespace SharpMinerals.Vanilla;

/// <summary>
/// The vanilla content "mod": registers the default blocks and items that used to be hardcoded built-ins in the
/// core registries. Because <see cref="ModLoader"/> sets <c>ModContent.CurrentNamespace = ModId</c> ("minecraft")
/// around <see cref="OnInitialize"/>, every <c>BlockRegistry.Register</c>/<c>ItemRegistry.Register</c> call here
/// lands in the <c>minecraft</c> namespace - no namespace forcing needed. The host must load this FIRST so its
/// blocks get the lowest palette ids right after the engine's air/missing.
/// </summary>
[ModInfo("minecraft", "1.0.0", ["SharpMinerals"], TargetServerVersion = "0.1.0")]
public sealed partial class VanillaMod : Mod {
    public override void OnInitialize() {
        // Register before the blocks that reference them in drop lambdas (lambdas are lazy, but keep the order tidy).
        Bedrock     = BlockRegistry.Register("bedrock");
        Cobblestone = BlockRegistry.Register("cobblestone").DropSelf();
        Stone       = BlockRegistry.Register("stone").Add(new DropBlockDescriptor(() => new ItemStack(Cobblestone)));
        Dirt        = BlockRegistry.Register("dirt").DropSelf();
        GrassBlock  = BlockRegistry.Register("grass_block").Add(new DropBlockDescriptor(() => new ItemStack(Dirt)));
        Chest       = BlockRegistry.Register("chest").DropSelf().Add(new ContainerBlockDescriptor(27), new StatesBlockDescriptor(State.Facing));
        Wool        = BlockRegistry.Register("wool").DropSelf().Add(new StatesBlockDescriptor(State.Color));
        Sand        = BlockRegistry.Register("sand").DropSelf().Add(new FallingBlockDescriptor());
        Gravel      = BlockRegistry.Register("gravel").DropSelf().Add(new FallingBlockDescriptor());
        RedSand     = BlockRegistry.Register("red_sand").DropSelf();
        // Red sand falls like sand - borrow the stateless falling behaviour (its drop stays its own, via DropSelf).
        RedSand.Copy<FallingBlockDescriptor>(Sand);
        // Water: a generated fluid for oceans/lakes. No drop, no item (placed via bucket, not a block item).
        Water       = BlockRegistry.Register("water");
        OakLog      = BlockRegistry.Register("oak_log").DropSelf();
        OakLeaves   = BlockRegistry.Register("oak_leaves").DropSelf();
        // Ground cover (non-solid plants). Flowers drop themselves; short grass drops nothing.
        ShortGrass  = BlockRegistry.Register("short_grass");
        Dandelion   = BlockRegistry.Register("dandelion").DropSelf();
        Poppy       = BlockRegistry.Register("poppy").DropSelf();
        Cornflower  = BlockRegistry.Register("cornflower").DropSelf();
        OxeyeDaisy  = BlockRegistry.Register("oxeye_daisy").DropSelf();
        DeadBush    = BlockRegistry.Register("dead_bush").DropSelf();
        RedSandstone = BlockRegistry.Register("red_sandstone").DropSelf();

        Stick       = ItemRegistry.Register("stick");

        // Overworld biomes (seeded factories the generator instantiates per world).
        Generator.VanillaBiomes.Register();

        // Vanilla wire-id mappings (per protocol version) for the data-driven TypeMapper - the core knows no ids.
        WireMappings.Register();
    }
}

/// <summary>The registered vanilla flyweights, assigned during <see cref="VanillaMod.OnInitialize"/>. They are
/// null until the mod loads - reference them only after the host/test bootstrap has loaded <see cref="VanillaMod"/>.</summary>
public sealed partial class VanillaMod {
    public static BlockType Bedrock { get; internal set; } = null!;
    public static BlockType Cobblestone { get; internal set; } = null!;
    public static BlockType Stone { get; internal set; } = null!;
    public static BlockType Dirt { get; internal set; } = null!;
    public static BlockType GrassBlock { get; internal set; } = null!;
    public static BlockType Chest { get; internal set; } = null!;
    public static BlockType Wool { get; internal set; } = null!;
    public static BlockType Sand { get; internal set; } = null!;
    public static BlockType Gravel { get; internal set; } = null!;
    public static BlockType RedSand { get; internal set; } = null!;
    public static BlockType Water { get; internal set; } = null!;
    public static BlockType OakLog { get; internal set; } = null!;
    public static BlockType OakLeaves { get; internal set; } = null!;
    public static BlockType ShortGrass { get; internal set; } = null!;
    public static BlockType Dandelion { get; internal set; } = null!;
    public static BlockType Poppy { get; internal set; } = null!;
    public static BlockType Cornflower { get; internal set; } = null!;
    public static BlockType OxeyeDaisy { get; internal set; } = null!;
    public static BlockType DeadBush { get; internal set; } = null!;
    public static BlockType RedSandstone { get; internal set; } = null!;
    public static ItemType Stick { get; internal set; } = null!;
}
