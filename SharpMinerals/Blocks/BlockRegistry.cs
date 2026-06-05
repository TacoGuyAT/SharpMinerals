using SharpMinerals.Items;
using SharpMinerals.Modding;

namespace SharpMinerals.Blocks;

/// <summary>The block palette: the dense <see cref="BlockType.BlockId"/> -> <see cref="BlockType"/> map that
/// chunk storage and the protocol state table index by. <see cref="Air"/> is palette id 0. Each block is also
/// registered as an item in <see cref="ItemRegistry"/> (every block is an item), so name/item lookups go there.
/// The only built-ins are the two engine primitives (<see cref="Air"/> + <see cref="Missing"/>) under the
/// <c>sharpminerals</c> namespace; ALL vanilla content (stone, dirt, ...) is registered by the
/// <c>SharpMinerals.Minecraft</c> mod under <c>minecraft</c>, like any other mod.</summary>
public static class BlockRegistry {
    static readonly List<BlockType> palette = new();
    static bool frozen;

    static BlockType Add(string ns, string name, bool isAir) {
        if (frozen)
            throw new InvalidOperationException(
                $"BlockRegistry is frozen - register block \"{name}\" during mod OnInitialize, before the palette is built.");
        int blockId = palette.Count;
        // Register as an item (unified id + lookup); the factory gets the item id + identifier, we supply the palette id.
        var block = ItemRegistry.Add(ns, name, (id, identifier) => new BlockType(id, blockId, identifier, isAir));
        palette.Add(block);
        return block;
    }

    /// <summary>Registers a new block, returning it for fluent composition. For mods - call from
    /// <see cref="Modding.Mod.OnInitialize"/>; throws once <see cref="Freeze">frozen</see>. Namespaced under the
    /// loading mod's id. A modded block's wire id falls back to stone until a type-mapping component is added.</summary>
    public static BlockType Register(string name, bool isAir = false) => Add(ModContent.CurrentNamespace, name, isAir);

    /// <summary>Seals the registry - the host calls this after mods init, before the palette is built.</summary>
    public static void Freeze() => frozen = true;

    /// <summary>The empty cell (palette id 0). The chunk store and <see cref="FromState"/> depend on this id.
    /// Registered by <see cref="CoreMod"/> (the engine mod, loaded first) - non-null after engine init.</summary>
    public static BlockType Air { get; internal set; } = null!;

    /// <summary>Placeholder for content the server can't represent (a dropped mod block, an unmappable type). It
    /// renders as stone on the wire (the type mapper's fallback) but is a distinct, non-air block. Registered by
    /// <see cref="CoreMod"/>.</summary>
    public static BlockType Missing { get; internal set; } = null!;

    /// <summary>All blocks, in palette order (index == <see cref="BlockType.BlockId"/>).</summary>
    public static IReadOnlyList<BlockType> All => palette;
    public static BlockType FromId(int blockId) => palette[blockId];
    public static BlockType FromState(ushort state) => palette[state];

    /// <summary>The block registered under <paramref name="name"/>, or null if the name is unregistered or a
    /// non-block item. Backed by the unified <see cref="ItemRegistry"/>.</summary>
    public static BlockType? FromName(string name) => ItemRegistry.FromName(name) as BlockType;
}
