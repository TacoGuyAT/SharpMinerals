namespace SharpMinerals.Level;

/// <summary>Engine-level world constants that the core depends on but that the (mod-owned) terrain generator also
/// honours. The flat-world surface height lives here so core spawn placement and the chunk heightmap don't have to
/// reference <c>FlatChunkGenerator</c> (which moved to the <c>SharpMinerals.Minecraft</c> mod). Known tech debt:
/// the chunk-serializer heightmap is a flat-world constant — fine for the superflat default, stale after edits.</summary>
public static class WorldDefaults {
    /// <summary>World Y of the topmost solid layer of the default flat world (grass). Entities stand at SurfaceY+1.</summary>
    public const int GrassY = 4;

    /// <summary>World Y a player spawns on in the default flat world.</summary>
    public const int SurfaceY = GrassY + 1;
}
