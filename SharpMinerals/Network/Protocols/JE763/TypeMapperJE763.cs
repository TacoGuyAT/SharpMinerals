using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Network.Protocols.JE763;

/// <summary>
/// <see cref="ITypeMapper"/> for Java Edition 1.20.1 (protocol 763): maps block/item definitions to
/// vanilla wire ids by internal name. Unmapped kinds fall back to a placeholder.
/// </summary>
public sealed class TypeMapperJE763 : ITypeMapper {
    static readonly ILogger Log = Logging.For<TypeMapperJE763>();
    const int FallbackStateId = 1; // minecraft:stone
    const int FallbackItemId = 1;  // minecraft:stone

    // 1.20.1 global-palette block-state ids.
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

    // 1.20.1 block-entity-type registry ids, for the chunk packet's block-entity list (so a chest renders on chunk load).
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
        ["stick"] = 807,
    };

    // Reverse map (vanilla item id → our definition) and state ids by BlockType.Id (O(1) on the serializer hot path).
    readonly Dictionary<int, ItemType> itemById;
    readonly int[] stateByBlockId;

    // Per-stateful-block layout: default id + a stride per modeled property (= product of value-counts of
    // vanilla properties after it). modeled id = default + Σ valueIndex * stride.
    readonly Dictionary<string, StateLayout> stateLayouts = new() {
        ["chest"] = new StateLayout(2955, (State.Facing, 6)), // facing(4) × type(3) × waterlogged(2); model facing ⇒ stride 6
    };

    // Overrides the linear formula can't express (values mapping to DIFFERENT vanilla blocks/items, e.g.
    // dye colour → 16 wools). Block side returns null to fall through to the formula.
    readonly Dictionary<string, Func<BlockState, int?>> stateOverrides;
    readonly Dictionary<string, Func<BlockState, int>> itemOverrides;

    public TypeMapperJE763() {
        itemById = BuildReverseItemTable();
        stateByBlockId = BuildStateTable();
        stateOverrides = new() { ["wool"] = s => 2047 + s.Get(State.Color) }; // white 2047 … black 2062
        itemOverrides = new() { ["wool"] = s => 180 + s.Get(State.Color) };   // white_wool 180 … black_wool 195
    }

    Dictionary<int, ItemType> BuildReverseItemTable() {
        _ = BlockRegistry.All; // force block registration (which, via ItemRegistry, also registers built-in items)
        var table = new Dictionary<int, ItemType>();
        foreach (var (name, id) in itemIdByName)
            if (ItemRegistry.FromName(name) is { } type) // any registered type — items too, not just blocks
                table[id] = type;
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

    public int StateId(BlockType block) => stateByBlockId[block.BlockId];

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
        // 1.20.1 spawns players via Spawn Player, not Spawn Entity (players carry an entity type only from 1.20.2+).
        "player" => throw new NotSupportedException(
            "JE763 spawns players via the dedicated Spawn Player packet, not Spawn Entity."),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type.Name, "No JE763 wire id for this entity type."),
    };

    public int BlockEntityTypeId(BlockEntity blockEntity) {
        if(blockEntityIdByName.TryGetValue(blockEntity.Type.Name, out var id)) {
            return id;
        }
        Log?.LogWarning("Invalid mapping for block entity {blockEntity} ({type})", blockEntity, blockEntity.Type);
        return 1;
    }

    public int ItemId(ItemType item) => itemIdByName.GetValueOrDefault(item.Name, FallbackItemId);

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
        // Wool colours are a contiguous vanilla range → our one wool block with Color set (inverse of itemOverrides["wool"]).
        if (vanillaId is >= 180 and <= 195)
            return new ItemStack(BlockRegistry.Wool)
                .WithState(new BlockState(BlockRegistry.Wool).Set(State.Color, vanillaId - 180));
        return FromItemId(vanillaId) is { } type ? new ItemStack(type) : default;
    }
}
