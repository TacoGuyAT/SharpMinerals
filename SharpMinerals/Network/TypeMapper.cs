using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Network;

/// <summary>
/// The single, data-driven block/item/entity -> wire-id mapper for ALL protocols (the Geyser/ViaVersion-style
/// seam). It hardcodes no vanilla ids: content owners register mappings via the static <see cref="Map{TMin}(string)"/>,
/// scoped to a span of protocol versions through the <see cref="Protocol"/> inheritance chain. Each protocol builds
/// a <see cref="TypeMapper"/> bound to its own type (<c>new TypeMapper(GetType())</c>) that resolves the applicable
/// mappings - most-derived <c>TMin</c> wins per facet, so a newer version's delta overrides the base, exactly like
/// the old inheritance chain but data-driven. The vanilla mappings live in the <c>SharpMinerals.Minecraft</c> mod.
/// </summary>
public sealed class TypeMapper {
    // -- Registration (static, global) ---------------------------------------
    static readonly List<Mapping> mappings = new();
    static bool frozen;

    /// <summary>Registers a wire mapping for content <paramref name="id"/>, applying to protocol
    /// <typeparamref name="TMin"/> and every protocol that extends it (open upper bound). On overlap the
    /// most-derived <typeparamref name="TMin"/> wins per facet, so a newer version's delta overrides the base.
    /// Returns a builder to set the state/item/entity ids.</summary>
    public static MappingBuilder Map<TMin>(string id) where TMin : Protocol =>
        Add(typeof(TMin), null, id);

    /// <summary>As <see cref="Map{TMin}(string)"/> but bounded to the inclusive protocol span
    /// <typeparamref name="TMin"/>..<typeparamref name="TMax"/> (content present only in a version range).</summary>
    public static MappingBuilder Map<TMin, TMax>(string id) where TMin : Protocol where TMax : TMin =>
        Add(typeof(TMin), typeof(TMax), id);

    static MappingBuilder Add(Type min, Type? max, string id) {
        if (frozen)
            throw new InvalidOperationException($"TypeMapper mappings are frozen - register \"{id}\" during mod OnInitialize.");
        var m = new Mapping(min, max, Identifier.Parse(id));
        mappings.Add(m);
        return new MappingBuilder(m);
    }

    /// <summary>Seals the mapping table - the host calls this after mods init, before protocols build their mappers.</summary>
    public static void Freeze() => frozen = true;

    // -- Per-protocol instance ------------------------------------------------
    const string Fallback = Identifier.EngineNamespace + ":missing"; // unmapped content renders as this (-> stone)

    readonly Dictionary<string, Resolved> byId = new();                            // Identifier.Full -> resolved facets
    readonly int[] stateByBlockId;                                                 // BlockType.BlockId -> default state (hot path)
    readonly Dictionary<int, (ItemType Type, BlockState? State)> itemById = new(); // reverse: wire item id -> content

    public TypeMapper(Type protocolType) {
        // Resolve the applicable mappings into a flat per-id table: for each identifier, each facet comes from the
        // most-derived applicable mapping that defines it (so deltas override the base, block-entity inherits, ...).
        foreach (var group in mappings.Where(m => Applies(m, protocolType)).GroupBy(m => m.Id.Full)) {
            var apps = group.ToList();
            byId[group.Key] = new Resolved(
                State: Best(apps, m => m.State is not null)?.State,
                Item: Best(apps, m => m.Item is not null)?.Item,
                BlockEntity: Best(apps, m => m.BlockEntity is not null)?.BlockEntity,
                Entity: Best(apps, m => m.Entity is not null)?.Entity);
        }
        stateByBlockId = BuildStateTable();
        BuildReverseItems();
    }

    static bool Applies(Mapping m, Type protocol) =>
        m.Min.IsAssignableFrom(protocol) && (m.Max is null || protocol.IsAssignableFrom(m.Max));

    // The most-derived Min among the applicable mappings that define a facet (later registration breaks ties).
    static Mapping? Best(List<Mapping> apps, Func<Mapping, bool> defines) {
        Mapping? best = null;
        foreach (var m in apps)
            if (defines(m) && (best is null || best.Min.IsAssignableFrom(m.Min)))
                best = m;
        return best;
    }

    // The resolved facets for a definition: its mapped identifier (a mod def's VanillaMapping is followed first),
    // or the `missing` fallback (-> stone), or empty (hard fallback to 0) when even that is unmapped.
    Resolved For(Identifier own, ComponentObject def) {
        var target = VanillaMapping.TargetOf(own, def);
        return byId.GetValueOrDefault(target.Full)
            ?? byId.GetValueOrDefault(Fallback)
            ?? Resolved.Empty;
    }

    int[] BuildStateTable() {
        var all = BlockRegistry.All; // forces block registration
        var table = new int[all.Count];
        for (int i = 0; i < all.Count; i++)
            table[i] = For(all[i].Id, all[i]).State?.Default ?? 0;
        return table;
    }

    void BuildReverseItems() {
        _ = BlockRegistry.All; // ensure content is registered before resolving names
        foreach (var (idStr, r) in byId) {
            if (r.Item is not { } im) continue;
            // Engine primitives (air/missing) aren't client-sendable items and would collide with the real content
            // they map to (missing->stone's id), so skip them in the reverse table.
            if (idStr.StartsWith(Identifier.EngineNamespace + ":", StringComparison.Ordinal)) continue;
            if (ItemRegistry.FromName(idStr) is not { } type) continue;

            if (im.Strides.Length == 0 || type is not BlockType bt) {
                itemById[im.Default] = (type, null);
            } else {
                // Expand a strided item across its property value-space (e.g. wool colour -> 180..195 <-> Color 0..15).
                var idx = new int[im.Strides.Length];
                while (true) {
                    int id = im.Default;
                    var state = new BlockState(bt);
                    for (int k = 0; k < im.Strides.Length; k++) {
                        id += idx[k] * im.Strides[k].Stride;
                        state.Set(im.Strides[k].Property, idx[k]);
                    }
                    itemById[id] = (bt, state);
                    int j = im.Strides.Length - 1;
                    while (j >= 0 && ++idx[j] >= im.Strides[j].Property.Count) idx[j--] = 0;
                    if (j < 0) break;
                }
            }
        }
    }

    // -- ITypeMapper-shaped surface (no interface - one unified class) ---------
    public int StateId(BlockType block) => stateByBlockId[block.BlockId];

    public int StateId(BlockState state) {
        if (For(state.Type.Id, state.Type).State is { } sm) {
            if (sm.Custom is { } custom) return custom(state);
            int id = sm.Default;
            foreach (var (property, stride) in sm.Strides)
                id += state.Get(property) * stride;
            return id;
        }
        return StateId(state.Type);
    }

    public int EntityTypeId(EntityType type) {
        if (For(type.Id, type).Entity is { } e) {
            if (e.ViaSpawnPlayer)
                throw new NotSupportedException(
                    "This entity spawns via the dedicated Spawn Player packet, not Spawn Entity.");
            if (e.WireId is { } id) return id;
        }
        throw new ArgumentOutOfRangeException(nameof(type), type.Id.Full, "No wire id for this entity type in this protocol.");
    }

    public int BlockEntityTypeId(BlockType block) => For(block.Id, block).BlockEntity ?? 0;

    /// <summary>The wire block-entity type id for <paramref name="block"/>, if it has one mapped (a mod maps a custom
    /// block entity via <see cref="MappingBuilder.BlockEntity"/>). False for a data-only block entity with no mapping,
    /// so the chunk serializer can skip it rather than send a bogus id.</summary>
    public bool TryBlockEntityTypeId(BlockType block, out int id) {
        if (For(block.Id, block).BlockEntity is { } mapped) { id = mapped; return true; }
        id = 0;
        return false;
    }

    public int ItemId(ItemType item) => For(item.Id, item).Item?.Default ?? 0;

    public int ItemId(ItemStack stack) {
        if (stack.Type is not { } type) return 0;
        if (For(type.Id, type).Item is not { } im) return 0;
        int id = im.Default;
        if (stack.State is { } state)
            foreach (var (property, stride) in im.Strides)
                id += state.Get(property) * stride;
        return id;
    }

    public ItemType? FromItemId(int wireId) => itemById.TryGetValue(wireId, out var e) ? e.Type : null;

    public ItemStack FromVanillaItem(int wireId) =>
        itemById.TryGetValue(wireId, out var e)
            ? e.State is { } st ? new ItemStack(e.Type).WithState(st) : new ItemStack(e.Type)
            : default;

    // -- Data model -----------------------------------------------------------
    internal sealed class Mapping(Type min, Type? max, Identifier id) {
        public Type Min { get; } = min;
        public Type? Max { get; } = max;
        public Identifier Id { get; } = id;
        public StateMap? State { get; set; }
        public ItemMap? Item { get; set; }
        public int? BlockEntity { get; set; }
        public EntitySpawn? Entity { get; set; }
    }

    internal sealed record StateMap(int Default, (State Property, int Stride)[] Strides, Func<BlockState, int>? Custom);
    internal sealed record ItemMap(int Default, (State Property, int Stride)[] Strides);
    internal sealed record EntitySpawn(int? WireId, bool ViaSpawnPlayer);

    sealed record Resolved(StateMap? State, ItemMap? Item, int? BlockEntity, EntitySpawn? Entity) {
        public static readonly Resolved Empty = new(null, null, null, null);
    }

    /// <summary>Fluent setter for a registered mapping (see <see cref="Map{TMin}(string)"/>).</summary>
    public sealed class MappingBuilder {
        readonly Mapping mapping;
        internal MappingBuilder(Mapping mapping) => this.mapping = mapping;

        /// <summary>The block's default block-state id, plus an optional linear layout: id = default + Sum valueIndex*stride.</summary>
        public MappingBuilder State(int id, params (State Property, int Stride)[] strides) {
            mapping.State = new StateMap(id, strides, null);
            return this;
        }

        /// <summary>A custom (non-linear) block-state -> wire-id function, for cases the linear layout can't express.</summary>
        public MappingBuilder State(int id, Func<BlockState, int> custom) {
            mapping.State = new StateMap(id, [], custom);
            return this;
        }

        /// <summary>The item id, plus an optional linear layout over item-identity properties (e.g. wool colour).</summary>
        public MappingBuilder Item(int id, params (State Property, int Stride)[] strides) {
            mapping.Item = new ItemMap(id, strides);
            return this;
        }

        public MappingBuilder BlockEntity(int id) { mapping.BlockEntity = id; return this; }
        public MappingBuilder Entity(int id) { mapping.Entity = new EntitySpawn(id, false); return this; }

        /// <summary>This entity spawns via the dedicated Spawn Player packet - EntityTypeId throws for it.</summary>
        public MappingBuilder EntityViaSpawnPlayer() { mapping.Entity = new EntitySpawn(null, true); return this; }
    }
}
