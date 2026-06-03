namespace SharpMinerals;

/// <summary>
/// A namespaced content identifier — Minecraft's <c>namespace:path</c> "resource location" as a value type.
/// <see cref="Namespace"/> is the owner (<c>minecraft</c> for built-ins, a mod's id for modded content) and
/// <see cref="Name"/> is the path. The full <c>namespace:path</c> string is computed ONCE in the constructor and
/// cached — it's the key used by registries, persistence, and vanilla wire mapping, so it's read often.
/// </summary>
public readonly struct Identifier : IEquatable<Identifier> {
    /// <summary>The namespace a bare path defaults to.</summary>
    public const string MinecraftNamespace = "minecraft";

    public string Namespace { get; }
    public string Name { get; }
    readonly string id; // cached "namespace:path"

    public Identifier(string ns, string name) {
        Namespace = ns;
        Name = name;
        id = $"{ns}:{name}";
    }

    /// <summary>Parses a <c>namespace:path</c> id; a bare path defaults to the <c>minecraft</c> namespace.</summary>
    public static Identifier Parse(string s) {
        int colon = s.IndexOf(':');
        return colon < 0 ? new Identifier(MinecraftNamespace, s) : new Identifier(s[..colon], s[(colon + 1)..]);
    }

    /// <summary>The full <c>namespace:path</c> string (cached).</summary>
    public string Full => id;

    public bool Equals(Identifier other) => id == other.id;
    public override bool Equals(object? obj) => obj is Identifier other && Equals(other);
    public override int GetHashCode() => id?.GetHashCode() ?? 0;
    public static bool operator ==(Identifier a, Identifier b) => a.Equals(b);
    public static bool operator !=(Identifier a, Identifier b) => !a.Equals(b);
}
