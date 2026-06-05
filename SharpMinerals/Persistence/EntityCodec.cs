using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Network.Buffers;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace SharpMinerals.Persistence;

/// <summary>Serializes a LIVE ECS entity's persistent components to a self-describing <c>byte[]</c>, and applies one
/// back onto an entity. Generic by design: it enumerates the entity's components (Arch <c>GetAllComponents</c>),
/// filters to those that persist (<c>IPersistentComponent</c>) and writes them through the shared length-prefixed
/// <see cref="ComponentBag"/> - so ANY entity kind (a player today, a mob later) saves the same way, with no codec
/// per kind. Unknown components (a removed mod's) round-trip through the bag and are re-attached on load as an
/// <c>UnresolvedComponentsEntityComponent</c> (non-destructive, like world recovery).</summary>
public static class EntityCodec {
    const byte Version = 1;

    public static byte[] Encode(ArchWorld ecs, ArchEntity entity) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms);
        s.WriteUByte(Version);
        var preserved = ecs.Has<UnresolvedComponentsEntityComponent>(entity)
            ? ecs.Get<UnresolvedComponentsEntityComponent>(entity).Components
            : null;
        ComponentBag.Write(s, ecs.GetAllComponents(entity), preserved);
        return ms.ToArray();
    }

    public static void Apply(ArchWorld ecs, ArchEntity entity, byte[] data) {
        using var ms = new MemoryStream(data, writable: false);
        var s = new MinecraftStream(ms);
        if (s.ReadUByte() is var version && version != Version)
            throw new NotSupportedException($"Unknown entity format version {version}.");

        // A blueprint-spawned entity already carries the components we're restoring, so Set in place; only a
        // component the current blueprint lacks (a mod added it after this save) needs a structural Add.
        var present = new HashSet<Type>();
        foreach (var c in ecs.GetAllComponents(entity))
            if (c is not null) present.Add(c.GetType());

        var unknown = ComponentBag.Read(s, component => {
            if (present.Add(component.GetType())) ecs.Add(entity, in component);
            else ecs.Set(entity, component);
        });
        if (unknown.Count > 0) {
            object holder = new UnresolvedComponentsEntityComponent(unknown);
            ecs.Add(entity, in holder);
        }
    }
}
