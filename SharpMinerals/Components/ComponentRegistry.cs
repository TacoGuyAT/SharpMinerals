using Arch.Core;
using System.Text;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Components;

/// <summary>Maps a namespaced id to/from a DATA component TYPE, and holds the read-factory for components that
/// persist (implement <see cref="IPersistentComponent"/>) - the basis of persistence: a saved component writes
/// its id + a length-prefixed blob, and load resolves the id back to a type and reconstructs it. Populated by the
/// generated module initializer the component source generator emits per assembly (one <see cref="Register{T}(string)"/>
/// /<see cref="Register{T}(string, Func{MinecraftStream, object})"/> call per <c>[Component]</c> type, namespaced
/// from the mod's <c>[ModInfo]</c>); sealed by <see cref="Modding.ModContent.Freeze"/>.</summary>
public static class ComponentRegistry {
    static readonly Dictionary<Type, string> ids = [];
    static readonly Dictionary<string, Type> byId = [];
    static readonly Dictionary<string, Func<MinecraftStream, object>> readers = [];
    static bool frozen;

    /// <summary>Registers a non-persistent component (an ECS struct, or a data component not yet made to persist):
    /// identity + its Arch component array. See the <see cref="Register{T}(string, Func{MinecraftStream, object})"/>
    /// overload for persistent components.</summary>
    public static void Register<T>(string @namespace) => Add<T>(@namespace);

    /// <summary>Registers a persistent component: as above, plus its <paramref name="read"/> factory (keyed by id)
    /// so the bag can reconstruct it on load. The generator emits this overload for any <c>[Component]</c> that
    /// implements <see cref="IPersistentComponent"/>, passing the type's <c>static Read</c>.</summary>
    public static void Register<T>(string @namespace, Func<MinecraftStream, object> read) =>
        readers[Add<T>(@namespace)] = read;

    static string Add<T>(string @namespace) {
        if (frozen)
            throw new InvalidOperationException($"ComponentRegistry is frozen - {typeof(T).Name} must register before ModContent.Freeze.");
        var type = typeof(T);
        var id = $"{@namespace}:{Snake(type.Name)}";
        if (byId.TryGetValue(id, out var existing) && existing != type)
            throw new InvalidOperationException($"Component id \"{id}\" is already registered to {existing.Name}.");
        ArrayRegistry.Add<T>();  // AOT-safe storage (harmless for a pure data component, which never lands in an archetype)
        ids[type] = id;
        byId[id] = type;
        return id;
    }

    /// <summary>The id of a registered component instance, or null if its type isn't registered.</summary>
    public static string? IdOf(object component) => ids.GetValueOrDefault(component.GetType());

    /// <summary>The component type for an id, or null if the id is unknown.</summary>
    public static Type? TypeOf(string id) => byId.GetValueOrDefault(id);

    /// <summary>The read-factory for a persistent component id, or null if the id is unknown / not persistent.</summary>
    public static Func<MinecraftStream, object>? ReaderFor(string id) => readers.GetValueOrDefault(id);

    public static void Freeze() => frozen = true;

    /// <summary>Type name to snake_case, suffix kept: <c>InventoryComponent</c> -&gt; <c>inventory_component</c>.</summary>
    static string Snake(string name) {
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++) {
            char c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
