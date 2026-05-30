namespace SharpMinerals.Components;

/// <summary>
/// A small, Arch-independent component container: a type-keyed set of component
/// objects. It backs both flyweight definitions (one <c>ItemType</c>/<c>BlockType</c>
/// per kind) and per-instance objects (<c>BlockEntity</c>, item-stack data), so custom
/// blocks/items are assembled by composition rather than subclassing. Reference type
/// with identity equality — definitions stay single-instance-per-kind.
/// </summary>
public class Composed {
    readonly Dictionary<Type, object> components = new();

    /// <summary>The component of type <typeparamref name="T"/> (throws if absent — guard with <see cref="Has{T}"/>/<see cref="TryGet{T}"/>).</summary>
    public T Get<T>() => (T)components[typeof(T)];

    /// <summary>True if a component of type <typeparamref name="T"/> is present.</summary>
    public bool Has<T>() => components.ContainsKey(typeof(T));

    /// <summary>Gets the component of type <typeparamref name="T"/> if present.</summary>
    public bool TryGet<T>(out T value) {
        if (components.TryGetValue(typeof(T), out var o)) {
            value = (T)o;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Adds or replaces a component and returns this (fluent composition).</summary>
    public Composed With<T>(T component) where T : notnull {
        components[typeof(T)] = component;
        return this;
    }

    /// <summary>The live components, for behavior dispatch.</summary>
    internal IEnumerable<object> Components => components.Values;
}
