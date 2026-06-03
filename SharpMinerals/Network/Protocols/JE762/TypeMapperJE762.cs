using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Network.Protocols.JE762;

/// <summary>
/// <see cref="ITypeMapper"/> for Java Edition 1.19.4 (protocol 762) — the base of the modern-Java mapper chain.
/// Maps block/item/entity definitions to vanilla wire ids by internal name; unmapped kinds fall back to a
/// placeholder. The lookup logic is version-independent; only the id tables differ, so <see cref="TypeMapperJE763"/>
/// extends this and overwrites the entries that the 1.20 additions shifted, then rebuilds the derived lookups.
/// </summary>
// Not sealed: TypeMapperJE763 extends this with the 1.20.1 id deltas.
public class TypeMapperJE762 : ITypeMapper {
    static readonly ILogger Log = Logging.For<TypeMapperJE762>();
    protected const int FallbackStateId = 1; // minecraft:stone
    protected const int FallbackItemId = 1;  // minecraft:stone

    // 1.19.4 global-palette block-state ids.
    protected readonly Dictionary<string, int> stateByName = new() {
        ["air"] = 0,
        ["bedrock"] = 79,
        ["stone"] = 1,
        ["dirt"] = 10,
        ["grass_block"] = 9,
        ["cobblestone"] = 14,
        ["chest"] = 2951, // minecraft:chest default block-state (facing north, single)
        ["wool"] = 2043,  // default = white_wool (colours handled by the override below)
        ["sand"] = 112,
        ["red_sand"] = 117,
        ["gravel"] = 118,
    };

    // 1.19.4 block-entity-type registry ids, for the chunk packet's block-entity list (so a chest renders on chunk load).
    protected readonly Dictionary<string, int> blockEntityIdByName = new() {
        ["chest"] = 1, // minecraft:chest
    };

    // 1.19.4 entity-type registry ids. Players carry no entity type here — they spawn via the dedicated packet.
    protected readonly Dictionary<string, int> entityIdByName = new() {
        ["item"] = 54,
        ["falling_block"] = 36,
    };

    // 1.19.4 item-registry ids.
    protected readonly Dictionary<string, int> itemIdByName = new() {
        ["bedrock"] = 43,
        ["stone"] = 1,
        ["dirt"] = 15,
        ["grass_block"] = 14,
        ["cobblestone"] = 22,
        ["chest"] = 275,
        ["wool"] = 179, // default = white_wool item (colours: 179 + colour index)
        ["sand"] = 44,
        ["red_sand"] = 46,
        ["gravel"] = 47,
        ["stick"] = 803,
    };

    // Reverse map (vanilla item id → our definition) and state ids by BlockType.Id (O(1) on the serializer hot path).
    // Rebuilt by a subclass after it applies its id deltas (see RebuildLookups).
    protected Dictionary<int, ItemType> itemById = new();
    protected int[] stateByBlockId = [];

    // Per-stateful-block layout: default id + a stride per modeled property (= product of value-counts of
    // vanilla properties after it). modeled id = default + Σ valueIndex * stride.
    protected readonly Dictionary<string, StateLayout> stateLayouts = new() {
        ["chest"] = new StateLayout(2951, (State.Facing, 6)), // facing(4) × type(3) × waterlogged(2); model facing ⇒ stride 6
    };

    // Overrides the linear formula can't express (values mapping to DIFFERENT vanilla blocks/items, e.g.
    // dye colour → 16 wools). Block side returns null to fall through to the formula.
    protected readonly Dictionary<string, Func<BlockState, int?>> stateOverrides;
    protected readonly Dictionary<string, Func<BlockState, int>> itemOverrides;

    public TypeMapperJE762() {
        stateOverrides = new() { ["wool"] = s => 2043 + s.Get(State.Color) }; // white 2043 … black 2058
        itemOverrides = new() { ["wool"] = s => 179 + s.Get(State.Color) };   // white_wool 179 … black_wool 194
        RebuildLookups();
    }

    /// <summary>Rebuilds the reverse-item and per-block state tables from the current id dictionaries. The base
    /// ctor calls it; a subclass calls it again after overwriting entries for its version.</summary>
    protected void RebuildLookups() {
        itemById = BuildReverseItemTable();
        stateByBlockId = BuildStateTable();
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
            table[i] = StateIdFor(VanillaMapping.TargetOf(all[i].Id, all[i]));
        return table;
    }

    // The per-version table lookups: a vanilla target resolves to its wire id; anything else falls back to stone.
    // (TargetOf already turns a modded definition into the vanilla content it maps to, or leaves it modded.)
    int StateIdFor(Identifier target) =>
        target.Namespace == "minecraft" ? stateByName.GetValueOrDefault(target.Name, FallbackStateId) : FallbackStateId;
    int ItemIdFor(Identifier target) =>
        target.Namespace == "minecraft" ? itemIdByName.GetValueOrDefault(target.Name, FallbackItemId) : FallbackItemId;

    protected sealed class StateLayout {
        public readonly int DefaultState;
        public readonly (State Property, int Stride)[] Strides;
        public StateLayout(int defaultState, params (State, int)[] strides) {
            DefaultState = defaultState;
            Strides = strides;
        }
    }

    public int StateId(BlockType block) => stateByBlockId[block.BlockId];

    public int StateId(BlockState state) {
        // Vanilla state mapping (1) per-block/state override → (2) the linear layout formula applies only to
        // minecraft blocks; everything else falls through to (3) the type's default (stone for modded blocks).
        if (state.Type.Id.Namespace == "minecraft") {
            if (stateOverrides.TryGetValue(state.Type.Id.Name, out var over) && over(state) is int forced)
                return forced;

            if (stateLayouts.TryGetValue(state.Type.Id.Name, out var layout)) {
                int id = layout.DefaultState;
                foreach (var (property, stride) in layout.Strides)
                    id += state.Get(property) * stride;
                return id;
            }
        }

        return StateId(state.Type);
    }

    public int EntityTypeId(EntityType type) {
        var target = VanillaMapping.TargetOf(type.Id, type);
        if (target.Namespace == "minecraft") {
            // Modern Java spawns players via the dedicated Spawn Player packet, not Spawn Entity
            // (players carry an entity type only from 1.20.2+).
            if (target.Name == "player")
                throw new NotSupportedException(
                    "Modern Java protocols spawn players via the dedicated Spawn Player packet, not Spawn Entity.");
            if (entityIdByName.TryGetValue(target.Name, out var id))
                return id;
        }
        throw new ArgumentOutOfRangeException(nameof(type), type.Id.Full, "No wire id for this entity type in this protocol.");
    }

    public int BlockEntityTypeId(BlockType block) {
        var target = VanillaMapping.TargetOf(block.Id, block);
        if(target.Namespace == "minecraft" && blockEntityIdByName.TryGetValue(target.Name, out var id)) {
            return id;
        }
        Log?.LogWarning("Invalid mapping for block entity {blockEntity}", block);
        return 1;
    }

    public int ItemId(ItemType item) => ItemIdFor(VanillaMapping.TargetOf(item.Id, item));

    public bool IsCustom(ItemType item) => item.Id.Namespace != "minecraft" || !itemIdByName.ContainsKey(item.Id.Name);

    public int ItemId(ItemStack stack) {
        if (stack.Type is not { } type)
            return FallbackItemId;
        if (stack.State is { } state && type.Id.Namespace == "minecraft" && itemOverrides.TryGetValue(type.Id.Name, out var over))
            return over(state);
        return ItemId(type);
    }

    public ItemType? FromItemId(int vanillaId) => itemById.GetValueOrDefault(vanillaId);

    /// <summary>The first vanilla item id of the contiguous 16-colour wool range (white … black), the inverse
    /// of <c>itemOverrides["wool"]</c>. A subclass shifts it when the 1.20 additions move the wool block.</summary>
    protected virtual int WoolItemBase => 179;

    public ItemStack FromVanillaItem(int vanillaId) {
        // Wool colours are a contiguous vanilla range → our one wool block with Color set (inverse of itemOverrides["wool"]).
        if (vanillaId >= WoolItemBase && vanillaId <= WoolItemBase + 15)
            return new ItemStack(BlockRegistry.Wool)
                .WithState(new BlockState(BlockRegistry.Wool).Set(State.Color, vanillaId - WoolItemBase));
        return FromItemId(vanillaId) is { } type ? new ItemStack(type) : default;
    }
}
