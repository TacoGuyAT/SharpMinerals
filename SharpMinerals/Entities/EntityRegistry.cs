using SharpMinerals.Components;
using SharpMinerals.Entities.Descriptors;

namespace SharpMinerals.Entities;

/// <summary>
/// The entity registry — flyweight <see cref="EntityType"/> definitions assembled from components,
/// mirroring <c>BlockRegistry</c>/<c>ItemRegistry</c>. Ids are assigned in registration order; the
/// wire id for each kind is a per-version concern in the type mapper, not here.
/// </summary>
public static class EntityRegistry {
    static readonly List<EntityType> byId = new();
    static readonly Dictionary<string, EntityType> byName = new();
    static bool frozen;

    static EntityType Define(string name) {
        if (frozen)
            throw new InvalidOperationException(
                $"EntityRegistry is frozen — register entity \"{name}\" during mod OnInitialize.");
        if (byName.ContainsKey(name))
            throw new ArgumentException($"An entity named \"{name}\" is already registered.", nameof(name));
        var type = new EntityType(byId.Count, name);
        byId.Add(type);
        byName[name] = type;
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
    public static EntityType? FromName(string name) => byName.GetValueOrDefault(name);
}
