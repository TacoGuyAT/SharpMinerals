using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Items.Components;

namespace SharpMinerals.Items;

/// <summary>Registry of non-block items (tools, food, …). Block-items live in <c>BlockRegistry</c>. Ids are
/// assigned in registration order.</summary>
public static class ItemRegistry {
    static readonly List<ItemType> byId = new();
    static readonly Dictionary<string, ItemType> byName = new();
    static bool frozen;

    static ItemType Define(string name, int maxStackSize = 64) {
        if (frozen)
            throw new InvalidOperationException(
                $"ItemRegistry is frozen — register item \"{name}\" during mod OnInitialize.");
        if (byName.ContainsKey(name))
            throw new ArgumentException($"An item named \"{name}\" is already registered.", nameof(name));
        var item = new ItemType(byId.Count, name);
        item.Add(new Stackable(maxStackSize));
        byId.Add(item);
        byName[name] = item;
        return item;
    }

    /// <summary>Registers a new (non-block) item, returning it for fluent composition. For mods — call from
    /// <c>Mod.OnInitialize</c>; throws once <see cref="Freeze">frozen</see>. Wire id falls back to stone
    /// until a type-mapping component is added.</summary>
    public static ItemType Register(string name, int maxStackSize = 64) => Define(name, maxStackSize);

    /// <summary>Seals the registry — the host calls this after mods init, before protocols are built.</summary>
    public static void Freeze() => frozen = true;

    public static IReadOnlyList<ItemType> All => byId;
    public static ItemType FromId(int id) => byId[id];
    public static ItemType? FromName(string name) => byName.GetValueOrDefault(name);

    /// <summary>Resolves a registered type by name across BOTH registries (every block is also an item),
    /// blocks first. Used to recover a custom item's identity from the marker the client echoes back.</summary>
    public static ItemType? Resolve(string name) => BlockRegistry.FromName(name) ?? FromName(name);
}
