using SharpMinerals.Components;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Persistence;

/// <summary>Serializes a set of persistent components (those implementing <see cref="IPersistentComponent"/>) as a
/// LENGTH-PREFIXED bag: <c>[VarInt count]</c>, then per component <c>[String id][VarInt blobLength][blob]</c>. The
/// length prefix is the point - a reader can skip a component whose id it no longer knows (a removed mod's) by
/// consuming the blob, with no schema and no stream desync. An unknown component's raw <c>[id][blob]</c> is also
/// PRESERVED (returned as a <see cref="RawComponent"/>) so the host can re-emit it on save - re-adding the mod
/// restores it, mirroring the unresolved-palette recovery in the chunk codec.
///
/// The core overloads work over any component source (a <see cref="ComponentObject"/>'s bag, a live ECS entity's
/// components), so block entities, item stacks and entities all share one wire format. The
/// <see cref="ComponentObject"/> convenience overloads keep the common call sites terse.</summary>
internal static class ComponentBag {
    // -- Generic source/sink (used by both ComponentObject hosts and the entity codec) -------------------------

    /// <summary>Writes every <see cref="IPersistentComponent"/> in <paramref name="components"/>, then re-emits any
    /// <paramref name="preserved"/> unknown blobs verbatim. The count covers both, so a reader is none the wiser.</summary>
    public static void Write(MinecraftStream s, IEnumerable<object?> components, IReadOnlyList<RawComponent>? preserved = null) {
        // Snapshot the persistent components first so the count is accurate.
        var persistent = new List<IPersistentComponent>();
        foreach (var component in components)
            if (component is IPersistentComponent p) persistent.Add(p);

        s.WriteVarInt(persistent.Count + (preserved?.Count ?? 0));
        foreach (var component in persistent) {
            var id = ComponentRegistry.IdOf(component)
                ?? throw new InvalidOperationException(
                    $"Component {component.GetType().Name} implements IPersistentComponent but is not registered - " +
                    "mark it [Component] and reference the component source generator so it gets an id.");
            s.WriteString(id);
            var blob = Encode(component);
            s.WriteVarInt(blob.Length);
            s.Write(blob, 0, blob.Length);
        }
        if (preserved is not null)
            foreach (var raw in preserved) {
                s.WriteString(raw.Id);
                s.WriteVarInt(raw.Blob.Length);
                s.Write(raw.Blob, 0, raw.Blob.Length);
            }
    }

    /// <summary>Reads the bag, handing each KNOWN component to <paramref name="set"/>. Each UNKNOWN id (no registered
    /// reader - a removed mod's) is collected as a <see cref="RawComponent"/> and returned, so the caller can stash it
    /// and re-emit it on the next save. Returns an empty list for the normal (all-known) case.</summary>
    public static List<RawComponent> Read(MinecraftStream s, Action<object> set) {
        int count = s.ReadVarInt();
        List<RawComponent>? unknown = null;
        for (int i = 0; i < count; i++) {
            var id = s.ReadString();
            int length = s.ReadVarInt();
            var blob = s.ReadBytes(length);
            if (ComponentRegistry.ReaderFor(id) is { } read) {
                using var ms = new MemoryStream(blob, writable: false);
                set(read(new MinecraftStream(ms)));
            } else {
                // Unknown id: the blob is already consumed (the length prefix makes the skip safe); keep it so it
                // round-trips - re-adding the mod makes the id known and it materialises as a real component again.
                (unknown ??= new()).Add(new RawComponent(id, blob));
            }
        }
        return unknown ?? [];
    }

    // -- ComponentObject convenience overloads (block entities, item-stack Data) -------------------------------

    /// <summary>Writes a <see cref="ComponentObject"/>'s persistent components, re-emitting any unknown blobs it is
    /// carrying (from a prior load) via an attached <see cref="UnresolvedComponents"/> holder. Null writes an empty bag.</summary>
    public static void Write(MinecraftStream s, ComponentObject? obj) {
        if (obj is null) { s.WriteVarInt(0); return; }
        var preserved = obj.TryGet<UnresolvedComponents>(out var holder) ? holder.Components : null;
        Write(s, obj.Components, preserved);
    }

    /// <summary>Reads the bag into <paramref name="obj"/>, attaching an <see cref="UnresolvedComponents"/> holder for
    /// any ids it no longer knows so they survive the next save.</summary>
    public static void Read(MinecraftStream s, ComponentObject obj) {
        var unknown = Read(s, obj.Set);
        if (unknown.Count > 0) obj.Set(new UnresolvedComponents(unknown));
    }

    static byte[] Encode(IPersistentComponent component) {
        using var ms = new MemoryStream();
        component.Write(new MinecraftStream(ms));
        return ms.ToArray();
    }
}

/// <summary>A persistent component whose id had no registered reader on load (a removed mod's): the raw id + opaque
/// blob, kept so it can be re-emitted verbatim on save (non-destructive round-trip).</summary>
internal readonly record struct RawComponent(string Id, byte[] Blob);
