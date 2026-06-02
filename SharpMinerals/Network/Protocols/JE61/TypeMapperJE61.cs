using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Network.Protocols.JE61;

/// <summary>
/// <see cref="ITypeMapper"/> for Java Edition 1.5.2 (protocol 61). Pre-1.13: blocks are flat numeric ids
/// (metadata nibble not modeled). Entity mapping and per-block metadata are future work and throw until needed.
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
    // 1.5.2 embeds tile entities in the legacy chunk format, not as a separate packet list, so none surface here.
    public int BlockEntityTypeId(BlockType block) => 0;
    public int ItemId(ItemStack stack) => stack.Type is { } t ? ItemId(t) : 0;
    public ItemType? FromItemId(int vanillaId) => blockById.GetValueOrDefault(vanillaId);
    public ItemStack FromVanillaItem(int vanillaId) => FromItemId(vanillaId) is { } t ? new ItemStack(t) : default;

    static NotImplementedException NotYet([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
        new($"JE61 type mapping ({member}) is not implemented yet.");

    public int EntityTypeId(EntityType type) => throw NotYet();
}
