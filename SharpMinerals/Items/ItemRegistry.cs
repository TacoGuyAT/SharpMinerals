using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Items.Components;
using SharpMinerals.Modding;

namespace SharpMinerals.Items;

/// <summary>The single registry of every item-type — plain items AND blocks (a <see cref="BlockType"/> is an
/// item too). Holds the unified item-id space and the name lookup; blocks additionally carry a separate palette
/// id in <see cref="BlockRegistry"/>. Ids are assigned in registration order.</summary>
public static class ItemRegistry {
    static readonly List<ItemType> byId = new();
    static readonly Dictionary<string, ItemType> byIdentifier = new(); // keyed by full namespaced Id
    static bool frozen;

    /// <summary>Registers a type under the <see cref="ModContent.CurrentNamespace">current namespace</see>,
    /// assigning its unified item id (its index here) and indexing it by full <c>namespace:path</c> id. The
    /// factory receives that id and the namespace so a subclass (e.g. <see cref="BlockType"/>) is built with
    /// them. Used by both item registration and <see cref="BlockRegistry"/>.</summary>
    internal static T Add<T>(string ns, string name, Func<int, Identifier, T> create) where T : ItemType {
        if (frozen)
            throw new InvalidOperationException(
                $"ItemRegistry is frozen — register \"{name}\" during mod OnInitialize.");
        var identifier = new Identifier(ns, name);
        string key = identifier.Full;
        if (byIdentifier.ContainsKey(key))
            throw new ArgumentException($"An item or block \"{key}\" is already registered.", nameof(name));
        var type = create(byId.Count, identifier);
        byId.Add(type);
        byIdentifier[key] = type;
        return type;
    }

    /// <summary>Registers a new (non-block) item, returning it for fluent composition. For mods — call from
    /// <see cref="Modding.Mod.OnInitialize"/>; throws once <see cref="Freeze">frozen</see>. Namespaced under the
    /// loading mod's id. Wire id falls back to stone until a type-mapping component is added.</summary>
    public static ItemType Register(string name, int maxStackSize = 64) =>
        Add(ModContent.CurrentNamespace, name, (id, identifier) => new ItemType(id, identifier).Add(new Stackable(maxStackSize)));

    /// <summary>Registers a built-in (<c>minecraft</c>-namespaced) item. Forces the namespace rather than reading
    /// <see cref="ModContent.CurrentNamespace"/>, because the static field initializers below can be triggered
    /// lazily during a mod's <c>OnInitialize</c> (when the ambient namespace is the mod's), which would otherwise
    /// mis-namespace every built-in and break vanilla wire mapping.</summary>
    static ItemType Builtin(string name, int maxStackSize = 64) =>
        Add(Identifier.MinecraftNamespace, name, (id, identifier) => new ItemType(id, identifier).Add(new Stackable(maxStackSize)));

    /// <summary>Seals the registry — the host calls this after mods init, before protocols are built.</summary>
    public static void Freeze() => frozen = true;

    public static IReadOnlyList<ItemType> All => byId;
    public static ItemType FromId(int id) => byId[id];

    /// <summary>Normalizes an identifier to its full string: a bare path (<c>stone</c>) gets the default
    /// <c>minecraft</c> namespace; a qualified <c>namespace:path</c> is used as-is. So old saves and command
    /// input work unprefixed.</summary>
    internal static string Normalize(string id) => id.IndexOf(':') >= 0 ? id : $"{Identifier.MinecraftNamespace}:{id}";

    /// <summary>The item-type (item or block) for <paramref name="id"/> — a bare path (defaults to
    /// <c>minecraft:</c>) or a full <c>namespace:path</c> — or null if unregistered.</summary>
    public static ItemType? FromName(string id) => byIdentifier.GetValueOrDefault(Normalize(id));

    // ── Built-in (non-block) items; blocks are defined in BlockRegistry and register themselves here too ──
    public static readonly ItemType Stick = Builtin("stick");
}
