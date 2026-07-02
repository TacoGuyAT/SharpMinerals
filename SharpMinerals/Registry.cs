using SharpMinerals.Modding;
using System.Diagnostics.CodeAnalysis;

namespace SharpMinerals;

public class Registry<T> {
    readonly List<T> values = [];
    readonly Dictionary<string, T> byIdentifier = [];
    public bool Frozen { get; private set; }
    public int Count => values.Count;
    public IReadOnlyList<T> All => values;

    public void Freeze() => Frozen = true;

    T Add(Identifier identifier, Func<int, Identifier, T> factory) {
        if(Frozen) {
            throw new InvalidOperationException($"Frozen; can't register \"{identifier}\".");
        }

        if(byIdentifier.ContainsKey(identifier.Full)) {
            throw new ArgumentException($"Duplicate: \"{identifier}\".");
        }

        T v = factory.Invoke(values.Count, identifier);
        values.Add(v);
        byIdentifier[identifier.Full] = v;

        return v;
    }

    public T Register(string name, Func<int, Identifier, T> factory) => Add(new(ModContent.CurrentNamespace, name), factory);

    public bool Contains(string path) => byIdentifier.ContainsKey(path);

    public bool TryFromPath(string path, [MaybeNullWhen(false)] out T result) => byIdentifier.TryGetValue(path, out result);

    public T this[int index] {
        get {
            if(values.Count < index) {
                throw new Exception($"Id {index} wasn't registered correctly");
            }
            return values[index];
        }
    }
}

public interface IRegistry { 
    void Freeze(); 
    bool Frozen { get; } 
}