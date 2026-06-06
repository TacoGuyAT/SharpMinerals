namespace SharpMinerals.Network.Nbt;

/// <summary>The biomes a mod contributes to the wire: their registry id (= registration order), climate, and
/// render colours. Both the login biome registry (<see cref="RegistryCodec"/>) and the per-chunk biome
/// encoding (<c>ChunkSerializer</c>) read from this, so the ids the client is told about and the ids the
/// chunks reference always agree. Mods register in their init phase, before the first client joins. Names are
/// short ("plains"); the emitted registry key is "minecraft:" + name. The wire-side counterpart to the
/// gameplay biomes in the generator, mirroring how <c>WireMappings</c> backs the block/item registries.</summary>
public static class BiomeWireRegistry {
    public readonly record struct Wire(
        string Name, float Temperature, float Downfall, bool HasPrecipitation,
        int SkyColor, int? GrassColor, int? FoliageColor, int WaterColor);

    static readonly List<Wire> entries = new();
    static readonly Dictionary<string, int> ids = new();

    public static IReadOnlyList<Wire> Entries => entries;

    public static void Register(string name, float temperature, float downfall, bool hasPrecipitation,
                                int skyColor, int? grassColor = null, int? foliageColor = null,
                                int waterColor = 4159204) {
        ids[name] = entries.Count;
        entries.Add(new Wire(name, temperature, downfall, hasPrecipitation, skyColor, grassColor, foliageColor, waterColor));
    }

    /// <summary>The wire id for a biome name (its registration order), or 0 (the first/default biome) when the
    /// name is unknown or null - e.g. a flat/void world whose generator exposes no biomes.</summary>
    public static int IdOf(string? name) => name is not null && ids.TryGetValue(name, out var id) ? id : 0;
}
