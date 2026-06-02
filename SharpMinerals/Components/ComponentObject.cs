namespace SharpMinerals.Components;

/// <summary>An object assembled from components, keyed by each component's runtime type. Backs flyweight
/// definitions (<c>ItemType</c>/<c>BlockType</c>) and per-instance objects, so kinds are built by composition.
/// Reference type with identity equality.</summary>
public class ComponentObject {
    readonly Dictionary<Type, object> components = new();

    /// <summary>The component of type <typeparamref name="T"/> (throws if absent — guard with <see cref="Has{T}"/>/<see cref="TryGet{T}"/>).</summary>
    public T Get<T>() => (T)components[typeof(T)];

    public bool Has<T>() => components.ContainsKey(typeof(T));

    public bool TryGet<T>(out T value) {
        if (components.TryGetValue(typeof(T), out var o)) {
            value = (T)o;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Adds or replaces a component (keyed by its runtime type).</summary>
    internal void Set(object component) => components[component.GetType()] = component;

    internal void Set<T>(T? component) {
        var type = typeof(T);
        if(component is null) {
            components.Remove(type);
        } else {
            components[type] = component;
        }
    }

    /// <summary>The live components, for behavior dispatch.</summary>
    internal IEnumerable<object> Components => components.Values;
}

/// <summary>Fluent composition helpers.</summary>
public static class ComponentObjects {
    /// <summary>Adds (or replaces) components and returns the same object still typed as
    /// <typeparamref name="T"/>, so additions chain without losing the type.</summary>
    public static T Add<T>(this T self, params object[] components) where T : ComponentObject {
        foreach (var component in components)
            self.Set(component);
        return self;
    }
}
