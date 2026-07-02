using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Items;
using SharpMinerals.Modding;
using SharpMinerals.Vanilla.Data;

namespace SharpMinerals.Vanilla;

/// <summary>
/// The vanilla content "mod": registers the default blocks and items that used to be hardcoded built-ins in the
/// core registries. Because <see cref="ModLoader"/> sets <c>ModContent.CurrentNamespace = ModId</c> ("minecraft")
/// around <see cref="OnInitialize"/>, every <c>BlockRegistry.Register</c>/<c>ItemRegistry.Register</c> call here
/// lands in the <c>minecraft</c> namespace - no namespace forcing needed. The host must load this FIRST so its
/// blocks get the lowest palette ids right after the engine's air/missing.
///
/// Content is now data-driven: <see cref="MinecraftData"/> supplies the full vanilla block/item set (identity +
/// per-protocol wire ids), so we register every block/item automatically, then enrich select ones with behavior
/// (drops, containers, falling, states) and bind the static accessors below via the returned <c>blocks</c> map.
/// </summary>
[ModInfo("minecraft", "1.0.0", ["SharpMinerals"], TargetServerVersion = "0.1.0")]
public sealed partial class VanillaMod : Mod {
    public override void OnInitialize() {
        var data = MinecraftData.Load();

        // Auto-register the whole vanilla set; `blocks` maps "minecraft:name" -> the registered block.
        var blocks = RegisterFromData(data);

        // wool is the ONE block the engine models differently: a single block with a synthetic Color state,
        // where minecraft-data has 16 separate *_wool blocks (skipped by RegisterFromData). Hand-register it.
        blocks["minecraft:wool"] = Wool =
            BlockType.Register("wool").DropSelf().Add(new StatesBlockDescriptor(State.Color));

        // Enrich select blocks with behavior and bind the static accessors worldgen/gameplay reference. `.Add`
        // returns the BlockType (replacing any auto component of the same type), so this both decorates and binds.
        Bedrock      = blocks["minecraft:bedrock"];
        Cobblestone  = blocks["minecraft:cobblestone"];
        Stone        = blocks["minecraft:stone"].Add(new DropBlockDescriptor(() => new ItemStack(Cobblestone)));
        Dirt         = blocks["minecraft:dirt"];
        GrassBlock   = blocks["minecraft:grass_block"].Add(new DropBlockDescriptor(() => new ItemStack(Dirt)));
        Chest        = blocks["minecraft:chest"].Add(new ContainerBlockDescriptor(27), new StatesBlockDescriptor(State.Facing));
        Sand         = blocks["minecraft:sand"].Add(new FallingBlockDescriptor());
        Gravel       = blocks["minecraft:gravel"].Add(new FallingBlockDescriptor());
        RedSand      = blocks["minecraft:red_sand"].Add(new FallingBlockDescriptor());
        RedSandstone = blocks["minecraft:red_sandstone"];
        Sandstone    = blocks["minecraft:sandstone"];
        Water        = blocks["minecraft:water"];
        OakLog       = blocks["minecraft:oak_log"];
        OakLeaves    = blocks["minecraft:oak_leaves"];
        ShortGrass   = blocks["minecraft:grass"]; // 1.20.1 names the short-grass block "grass" (renamed to short_grass in 1.20.3)
        Dandelion    = blocks["minecraft:dandelion"];
        Poppy        = blocks["minecraft:poppy"];
        Cornflower   = blocks["minecraft:cornflower"];
        OxeyeDaisy   = blocks["minecraft:oxeye_daisy"];
        DeadBush     = blocks["minecraft:dead_bush"];

        if(ItemType.TryFromPath("minecraft:stick", out var stick)) {
            Stick = stick;
        }

        // Overworld biomes (seeded factories the generator instantiates per world).
        Generator.VanillaBiomes.Register();

        // Vanilla wire-id mappings (per protocol version) for the data-driven TypeMapper - driven by the same data.
        WireMappings.Register(data);
    }

    /// <summary>Registers every vanilla block and item from <paramref name="data"/> (the 1.20.1 set), skipping the
    /// engine-owned air family and the <c>*_wool</c> blocks (engine models one <c>wool</c>). Each diggable block
    /// with an item drops itself by default; behavioral components are added afterwards via the returned map. Block
    /// items are created by <see cref="BlockRegistry"/>, so the item pass adds only the non-block items.</summary>
    static Dictionary<string, BlockType> RegisterFromData(MinecraftData data) {
        var blocks = new Dictionary<string, BlockType>();
        foreach (var b in data.V763.Blocks) {
            if (Skip(b.Name)) continue;
            var block = BlockType.Register(b.Name);
            if (b.Diggable && data.V763.ItemId.ContainsKey(b.Name)) block.DropSelf();
            blocks[block.Id.Full] = block;
        }
        foreach (var it in data.V763.Items) {
            if (Skip(it.Name)) continue;
            if (ItemType.Registry.Contains($"minecraft:{it.Name}")) continue; // already registered as a block item
            ItemType.Register(it.Name, it.StackSize);
        }
        return blocks;
    }

    /// <summary>Names excluded from auto-registration: the engine already owns <c>air</c> (palette 0), and the
    /// <c>*_wool</c> family is collapsed into one engine-modeled <c>wool</c> block.</summary>
    static bool Skip(string name) =>
        name is "air" or "cave_air" or "void_air" || name.EndsWith("_wool", StringComparison.Ordinal);
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
    public static BlockType Sandstone { get; internal set; } = null!;
    public static ItemType Stick { get; internal set; } = null!;
}
