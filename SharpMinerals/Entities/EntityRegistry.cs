using SharpMinerals.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Modding;

namespace SharpMinerals.Entities;

/// <summary>The entity registry — flyweight <see cref="EntityType"/> definitions assembled from components,
/// mirroring <c>BlockRegistry</c>/<c>ItemRegistry</c>. Ids are assigned in registration order.</summary>
public static class EntityRegistry {
    static readonly List<EntityType> byId = new();
    static readonly Dictionary<string, EntityType> byIdentifier = new(); // keyed by full namespaced Id
    static bool frozen;

    static EntityType Define(string name) {
        if (frozen)
            throw new InvalidOperationException(
                $"EntityRegistry is frozen — register entity \"{name}\" during mod OnInitialize.");
        var identifier = new Identifier(ModContent.CurrentNamespace, name);
        string key = identifier.Full;
        if (byIdentifier.ContainsKey(key))
            throw new ArgumentException($"An entity \"{key}\" is already registered.", nameof(name));
        var type = new EntityType(byId.Count, identifier);
        byId.Add(type);
        byIdentifier[key] = type;
        return type;
    }

    /// <summary>Registers a new entity kind, returning it for fluent composition (<c>.Add(...)</c>).
    /// For mods — call from <c>Mod.OnInitialize</c>; throws once <see cref="Freeze">frozen</see>. A modded
    /// kind needs a wire id in the type mapper before it can be spawned to clients.</summary>
    public static EntityType Register(string name) => Define(name);

    /// <summary>Seals the registry — the host calls this after mods init, before protocols are built.</summary>
    public static void Freeze() => frozen = true;

    public static readonly EntityType Item         = Define("item");
    public static readonly EntityType Player       = Define("player").Add(new HealthEntityDescriptor(MaxHealth: 20f));
    public static readonly EntityType FallingBlock = Define("falling_block");

    public static IReadOnlyList<EntityType> All => byId;
    public static EntityType FromId(int id) => byId[id];

    /// <summary>The entity kind for <paramref name="id"/> — a bare path (defaults to <c>minecraft:</c>) or a
    /// full <c>namespace:path</c> — or null if unregistered.</summary>
    public static EntityType? FromName(string id) => byIdentifier.GetValueOrDefault(ItemRegistry.Normalize(id));
}
