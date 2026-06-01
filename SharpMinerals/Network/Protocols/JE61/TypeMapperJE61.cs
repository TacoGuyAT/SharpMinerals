using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Network.Protocols.JE61;

/// <summary>
/// <see cref="ITypeMapper"/> for Java Edition 1.5.2 (protocol 61). 1.5.2 is pre-1.13: blocks are flat
/// numeric byte ids (with a separate nibble of metadata, not modeled here yet). Block ids are sourced
/// by NAME from the legacy numeric registry (same scheme as minecraft-data 1.7). Item/entity mapping
/// and per-block metadata (e.g. wool colour) are future work; those members throw until needed.
/// </summary>
public sealed class TypeMapperJE61 : ITypeMapper {
    const int FallbackId = 1; // stone

    // 1.5.2 flat block ids (pre-1.13). Wool/chest carry metadata/state not modeled here yet.
    readonly Dictionary<string, int> blockIdByName = new() {
        ["air"] = 0,
        ["stone"] = 1,
        ["grass_block"] = 2,
        ["dirt"] = 3,
        ["cobblestone"] = 4,
        ["bedrock"] = 7,
        ["wool"] = 35,   // colour is metadata (defaults to white until metadata is modeled)
        ["chest"] = 54,
    };

    // Reverse map (legacy id → our block), for resolving a creative client's held item on placement.
    readonly Dictionary<int, BlockType> blockById;

    public TypeMapperJE61() {
        blockById = new();
        foreach (var (name, id) in blockIdByName)
            if (BlockRegistry.FromName(name) is { } block)
                blockById[id] = block;
    }

    public int StateId(BlockType block) => blockIdByName.GetValueOrDefault(block.Name, FallbackId);
    public int StateId(BlockState state) => StateId(state.Type);
    public int ItemId(ItemType item) => blockIdByName.GetValueOrDefault(item.Name, FallbackId);
    public bool IsCustom(ItemType item) => !blockIdByName.ContainsKey(item.Name);
    public bool TryBlockEntityTypeId(BlockType block, out int id) { id = 0; return false; } // legacy uses its own chunk format
    public int ItemId(ItemStack stack) => stack.Type is { } t ? ItemId(t) : 0;
    public ItemType? FromItemId(int vanillaId) => blockById.GetValueOrDefault(vanillaId);
    public ItemStack FromVanillaItem(int vanillaId) => FromItemId(vanillaId) is { } t ? new ItemStack(t) : default;

    static NotImplementedException NotYet([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
        new($"JE61 type mapping ({member}) is not implemented yet.");

    public int EntityTypeId(EntityType type) => throw NotYet();
}
