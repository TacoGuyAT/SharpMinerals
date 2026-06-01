using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Network.Protocols.JE763;

/// <summary>
/// <see cref="ITypeMapper"/> for Java Edition 1.20.1 (protocol 763): maps SharpMinerals block/item
/// definitions to vanilla wire ids, keyed by the definition's internal name. Game-side types carry
/// no vanilla ids — the mapping is protocol-version data and lives here. Unmapped kinds fall back to
/// a placeholder so a stock client still renders something.
/// </summary>
public sealed class TypeMapperJE763 : ITypeMapper {
    const int FallbackStateId = 1; // minecraft:stone
    const int FallbackItemId = 1;  // minecraft:stone

    // 1.20.1 global-palette block-state ids (bedrock/cobblestone fall back to stone until the
    // palette is fleshed out).
    readonly Dictionary<string, int> stateByName = new() {
        ["air"] = 0,
        ["bedrock"] = 79,
        ["stone"] = 1,
        ["dirt"] = 10,
        ["grass_block"] = 9,
        ["cobblestone"] = 14,
        ["chest"] = 2955, // minecraft:chest default block-state (facing north, single)
        ["wool"] = 2047,  // default = white_wool (colours handled by the override below)
        ["sand"] = 112,
        ["gravel"] = 118,
    };

    // 1.20.1 block-entity-type registry ids (furnace 0, chest 1, …) — for the chunk packet's block-entity
    // list, so a chest renders its (block-entity-rendered) model on chunk load, not only after an update.
    readonly Dictionary<string, int> blockEntityIdByName = new() {
        ["chest"] = 1, // minecraft:chest
    };

    // 1.20.1 item-registry ids.
    readonly Dictionary<string, int> itemIdByName = new() {
        ["bedrock"] = 43,
        ["stone"] = 1,
        ["dirt"] = 15,
        ["grass_block"] = 14,
        ["cobblestone"] = 22,
        ["chest"] = 277,
        ["wool"] = 180, // default = white_wool item (colours: 180 + colour index)
        ["sand"] = 44,
        ["gravel"] = 48,
    };

    // Reverse map (vanilla item id → our definition) and block-state ids by BlockType.Id (O(1) on
    // the per-cell serializer hot path) — both built in the constructor.
    readonly Dictionary<int, ItemType> itemById;
    readonly int[] stateByBlockId;

    // Per-stateful-block vanilla layout: default id + a stride per modeled property (stride =
    // product of the value-counts of the vanilla properties AFTER it). modeled id = default +
    // Σ valueIndex * stride.
    readonly Dictionary<string, StateLayout> stateLayouts = new() {
        ["chest"] = new StateLayout(2955, (State.Facing, 6)), // facing(4) × type(3) × waterlogged(2); we model facing ⇒ stride 6
    };

    // Overrides the linear formula can't express (a property whose values map to DIFFERENT vanilla
    // blocks/items — dye colour → 16 wools). Block side returns null to fall through to the formula.
    readonly Dictionary<string, Func<BlockState, int?>> stateOverrides;
    readonly Dictionary<string, Func<BlockState, int>> itemOverrides;

    public TypeMapperJE763() {
        itemById = BuildReverseItemTable();
        stateByBlockId = BuildStateTable();
        stateOverrides = new() { ["wool"] = s => 2047 + s.Get(State.Color) }; // white 2047 … black 2062
        itemOverrides = new() { ["wool"] = s => 180 + s.Get(State.Color) };   // white_wool 180 … black_wool 195
    }

    Dictionary<int, ItemType> BuildReverseItemTable() {
        var table = new Dictionary<int, ItemType>();
        foreach (var (name, id) in itemIdByName)
            if (BlockRegistry.FromName(name) is { } block)
                table[id] = block;
        return table;
    }

    int[] BuildStateTable() {
        var all = BlockRegistry.All; // forces BlockRegistry init first
        var table = new int[all.Count];
        for (int i = 0; i < all.Count; i++)
            table[i] = stateByName.GetValueOrDefault(all[i].Name, FallbackStateId);
        return table;
    }

    sealed class StateLayout {
        public readonly int DefaultState;
        public readonly (State Property, int Stride)[] Strides;
        public StateLayout(int defaultState, params (State, int)[] strides) {
            DefaultState = defaultState;
            Strides = strides;
        }
    }

    public int StateId(BlockType block) => stateByBlockId[block.Id];

    public int StateId(BlockState state) {
        // 1) per-block/state override → 2) the linear layout formula → 3) the type's default.
        if (stateOverrides.TryGetValue(state.Type.Name, out var over) && over(state) is int forced)
            return forced;

        if (stateLayouts.TryGetValue(state.Type.Name, out var layout)) {
            int id = layout.DefaultState;
            foreach (var (property, stride) in layout.Strides)
                id += state.Get(property) * stride;
            return id;
        }

        return StateId(state.Type);
    }

    public int EntityTypeId(EntityType type) => type.Name switch {
        "item" => 54, // minecraft:item
        "falling_block" => 36, // minecraft:falling_block
        // 1.20.1 spawns players via the dedicated Spawn Player packet, not Spawn Entity; players
        // only carry an entity type in Spawn Entity from 1.20.2+ (a future protocol's mapper).
        "player" => throw new NotSupportedException(
            "JE763 spawns players via the dedicated Spawn Player packet, not Spawn Entity."),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type.Name, "No JE763 wire id for this entity type."),
    };

    public bool TryBlockEntityTypeId(BlockType block, out int id) => blockEntityIdByName.TryGetValue(block.Name, out id);

    public int ItemId(ItemType item) => itemIdByName.GetValueOrDefault(item.Name, FallbackItemId);

    // A type we have no vanilla item id for (e.g. a mod-added block) renders as the fallback item.
    public bool IsCustom(ItemType item) => !itemIdByName.ContainsKey(item.Name);

    public int ItemId(ItemStack stack) {
        if (stack.Type is not { } type)
            return FallbackItemId;
        if (stack.State is { } state && itemOverrides.TryGetValue(type.Name, out var over))
            return over(state);
        return ItemId(type);
    }

    public ItemType? FromItemId(int vanillaId) => itemById.GetValueOrDefault(vanillaId);

    public ItemStack FromVanillaItem(int vanillaId) {
        // Wool colours occupy a contiguous vanilla item range → our one wool block with the Color
        // state set (the inverse of itemOverrides["wool"]).
        if (vanillaId is >= 180 and <= 195)
            return new ItemStack(BlockRegistry.Wool)
                .WithState(new BlockState(BlockRegistry.Wool).Set(State.Color, vanillaId - 180));
        return FromItemId(vanillaId) is { } type ? new ItemStack(type) : default;
    }
}
