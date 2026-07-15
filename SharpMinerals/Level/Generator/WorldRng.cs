namespace SharpMinerals.Level.Generator;

/// <summary>Deterministic, stateless per-position randomness for generation: a value hashed from the world seed,
/// a column (x, z), and a caller-chosen <c>salt</c> that separates independent rolls at the same column (jitter
/// vs rarity vs a block pick). Pure - the same inputs always give the same value, on any thread, in any order -
/// so features place identically no matter which cube stamps them. This replaces the hand-rolled per-decorator
/// <c>Hash01</c> helpers; the mixing constants are theirs verbatim, so results are bit-for-bit unchanged.</summary>
public readonly struct WorldRng {
    readonly int seed, x, z;

    WorldRng(int seed, int x, int z) {
        this.seed = seed;
        this.x = x;
        this.z = z;
    }

    /// <summary>A generator anchored at world column (x, z). Draw independent rolls from it with distinct salts.</summary>
    public static WorldRng At(int seed, int x, int z) => new(seed, x, z);

    /// <summary>A uniform value in [0, 1) for this column and <paramref name="salt"/>.</summary>
    public double Value(int salt) {
        uint h = (uint)(x * 374761393) ^ (uint)(z * 668265263) ^ (uint)(seed * 2246822519) ^ (uint)(salt * 3266489917);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / 4294967296.0;
    }

    /// <summary>An integer in [0, <paramref name="countExclusive"/>) for this column and <paramref name="salt"/>.</summary>
    public int Range(int salt, int countExclusive) => (int)(Value(salt) * countExclusive);

    /// <summary>True with probability <paramref name="probability"/> for this column and <paramref name="salt"/>.</summary>
    public bool Chance(int salt, double probability) => Value(salt) < probability;
}
