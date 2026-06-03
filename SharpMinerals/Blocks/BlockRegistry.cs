using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Items;

namespace SharpMinerals.Blocks;

/// <summary>The block palette: the dense <see cref="BlockType.BlockId"/> → <see cref="BlockType"/> map that
/// chunk storage and the protocol state table index by. Air is palette id 0. Each block is also registered as
/// an item in <see cref="ItemRegistry"/> (every block is an item), so name/item lookups go through there.
/// Drop overrides use a lazy <c>() =&gt;</c> lambda so a block can reference a kind registered later.</summary>
public static class BlockRegistry {
    static readonly List<BlockType> palette = new();
    static bool frozen;

    // Explicit (non-beforefieldinit) static ctor so ANY member access — incl. FromName, which now only reads
    // ItemRegistry — first runs the block field initializers below, registering the built-ins into both stores.
    // It also wires up cross-block component borrowing once every field is set (e.g. RedSand copying Sand's fall).
    static BlockRegistry() {
        // Red sand falls exactly like sand — borrow the (stateless) falling behaviour via the component Copy API
        // rather than re-declaring it. Its drop is its own (DropSelf), so Sand's drop descriptor is NOT copied.
        RedSand.Copy<FallingBlockDescriptor>(Sand);
    }

    static BlockType Define(string name, bool isAir = false) {
        if (frozen)
            throw new InvalidOperationException(
                $"BlockRegistry is frozen — register block \"{name}\" during mod OnInitialize, before the palette is built.");
        int blockId = palette.Count;
        // Register as an item (unified id + lookup); the factory gets the item id + identifier, we supply the palette id.
        var block = ItemRegistry.Add(name, (id, identifier) => new BlockType(id, blockId, identifier, isAir));
        palette.Add(block);
        return block;
    }

    /// <summary>Registers a new block, returning it for fluent composition. For mods — call from
    /// <see cref="Modding.Mod.OnInitialize"/>; throws once <see cref="Freeze">frozen</see>. A modded block's
    /// wire id falls back to stone until a type-mapping component is added.</summary>
    public static BlockType Register(string name) => Define(name);

    /// <summary>Seals the registry — the host calls this after mods init, before the palette is built.</summary>
    public static void Freeze() => frozen = true;

    public static readonly BlockType Air         = Define("air", isAir: true);
    public static readonly BlockType Bedrock     = Define("bedrock");
    public static readonly BlockType Cobblestone = Define("cobblestone").DropSelf();
    public static readonly BlockType Stone       = Define("stone").Add(new DropBlockDescriptor(() => new ItemStack(Cobblestone)));
    public static readonly BlockType Dirt        = Define("dirt").DropSelf();
    public static readonly BlockType GrassBlock  = Define("grass_block").Add(new DropBlockDescriptor(() => new ItemStack(Dirt)));
    public static readonly BlockType Chest       = Define("chest").DropSelf().Add(new ContainerBlockDescriptor(27), new StatesBlockDescriptor(State.Facing));
    public static readonly BlockType Wool        = Define("wool").DropSelf().Add(new StatesBlockDescriptor(State.Color));
    public static readonly BlockType Sand        = Define("sand").DropSelf().Add(new FallingBlockDescriptor());
    public static readonly BlockType Gravel      = Define("gravel").DropSelf().Add(new FallingBlockDescriptor());
    public static readonly BlockType RedSand     = Define("red_sand").DropSelf(); // falling behaviour copied from Sand in the static ctor

    /// <summary>All blocks, in palette order (index == <see cref="BlockType.BlockId"/>).</summary>
    public static IReadOnlyList<BlockType> All => palette;
    public static BlockType FromId(int blockId) => palette[blockId];
    public static BlockType FromState(ushort state) => palette[state];

    /// <summary>The block registered under <paramref name="name"/>, or null if the name is unregistered or a
    /// non-block item. Backed by the unified <see cref="ItemRegistry"/>.</summary>
    public static BlockType? FromName(string name) => ItemRegistry.FromName(name) as BlockType;
}
