using SharpMinerals.Blocks;
using SharpMinerals.Items;

namespace SharpMinerals.Network.Protocols.JE763;

/// <summary>
/// Translates SharpMinerals block/item definitions to Java Edition 1.20.1 wire ids
/// (the Geyser/ViaVersion-style seam). Game-side types carry no vanilla ids — the
/// mapping is protocol-version data and lives in the network layer, keyed by the
/// definition's internal name. Unmapped kinds fall back to a placeholder so a stock
/// client still renders something.
/// </summary>
public static class TypeMapper {
    const int FallbackStateId = 1; // minecraft:stone
    const int FallbackItemId = 1;  // minecraft:stone

    // 1.20.1 global-palette block-state ids (bedrock/cobblestone fall back to stone
    // until the palette is fleshed out).
    static readonly Dictionary<string, int> stateByName = new() {
        ["air"] = 0, 
        ["bedrock"] = 31, 
        ["stone"] = 1,
        ["dirt"] = 10, 
        ["grass_block"] = 9,
        ["cobblestone"] = 14,
        ["chest"] = 2955, // minecraft:chest default block-state (facing north, single)
    };

    // 1.20.1 item-registry ids.
    static readonly Dictionary<string, int> itemIdByName = new() {
        ["stone"] = 43,
        ["stone"] = 1,
        ["dirt"] = 15, 
        ["grass_block"] = 14, 
        ["cobblestone"] = 22,
        ["chest"] = 277,
    };

    // Reverse map: vanilla item id -> our item definition, for creative/click slots the
    // client sends back.
    static readonly Dictionary<int, ItemType> itemById = BuildReverseItemTable();

    static Dictionary<int, ItemType> BuildReverseItemTable() {
        var table = new Dictionary<int, ItemType>();
        foreach (var (name, id) in itemIdByName)
            if (BlockRegistry.FromName(name) is { } block)
                table[id] = block;
        return table;
    }

    // Block-state ids indexed by BlockType.Id — O(1) for the per-cell serializer hot path.
    static readonly int[] stateByBlockId = BuildStateTable();

    static int[] BuildStateTable() {
        var all = BlockRegistry.All; // forces BlockRegistry init (its two phases) first
        var table = new int[all.Count];
        for (int i = 0; i < all.Count; i++)
            table[i] = stateByName.GetValueOrDefault(all[i].Name, FallbackStateId);
        return table;
    }

    /// <summary>The 1.20.1 block-state id for a block (hot path: array index by block id).</summary>
    public static int StateId(BlockType block) => stateByBlockId[block.Id];

    /// <summary>The 1.20.1 item id for an item (by name; placeholder if unmapped).</summary>
    public static int ItemId(ItemType item) => itemIdByName.GetValueOrDefault(item.Name, FallbackItemId);

    /// <summary>Our item definition for a vanilla item id, or null if unmapped.</summary>
    public static ItemType? FromItemId(int vanillaId) => itemById.GetValueOrDefault(vanillaId);
}
