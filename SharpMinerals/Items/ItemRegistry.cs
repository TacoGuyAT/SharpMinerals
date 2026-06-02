using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Items.Components;

namespace SharpMinerals.Items;

/// <summary>The single registry of every item-type — plain items AND blocks (a <see cref="BlockType"/> is an
/// item too). Holds the unified item-id space and the name lookup; blocks additionally carry a separate palette
/// id in <see cref="BlockRegistry"/>. Ids are assigned in registration order.</summary>
public static class ItemRegistry {
    static readonly List<ItemType> byId = new();
    static readonly Dictionary<string, ItemType> byName = new();
    static bool frozen;

    /// <summary>Registers a type, assigning its unified item id (its index here) and indexing it by name. The
    /// factory receives that id so a subclass (e.g. <see cref="BlockType"/>) can be constructed with it. Used by
    /// both item registration and <see cref="BlockRegistry"/>.</summary>
    internal static T Add<T>(string name, Func<int, T> create) where T : ItemType {
        if (frozen)
            throw new InvalidOperationException(
                $"ItemRegistry is frozen — register \"{name}\" during mod OnInitialize.");
        if (byName.ContainsKey(name))
            throw new ArgumentException($"An item or block named \"{name}\" is already registered.", nameof(name));
        var type = create(byId.Count);
        byId.Add(type);
        byName[name] = type;
        return type;
    }

    /// <summary>Registers a new (non-block) item, returning it for fluent composition. For mods — call from
    /// <see cref="Modding.Mod.OnInitialize"/>; throws once <see cref="Freeze">frozen</see>. Wire id falls back
    /// to stone until a type-mapping component is added.</summary>
    public static ItemType Register(string name, int maxStackSize = 64) =>
        Add(name, id => new ItemType(id, name).Add(new Stackable(maxStackSize)));

    /// <summary>Seals the registry — the host calls this after mods init, before protocols are built.</summary>
    public static void Freeze() => frozen = true;

    public static IReadOnlyList<ItemType> All => byId;
    public static ItemType FromId(int id) => byId[id];

    /// <summary>The item-type (item or block) registered under <paramref name="name"/>, or null.</summary>
    public static ItemType? FromName(string name) => byName.GetValueOrDefault(name);

    // ── Built-in (non-block) items; blocks are defined in BlockRegistry and register themselves here too ──
    public static readonly ItemType Stick = Register("stick");
}
