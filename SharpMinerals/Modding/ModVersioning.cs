using NuGet.Versioning;

namespace SharpMinerals.Modding;

/// <summary>Mod version-compatibility helpers over <see cref="NuGet.Versioning.SemanticVersion"/>.</summary>
public static class ModVersioning {
    /// <summary>Whether a server at <paramref name="server"/> can host a mod built for
    /// <paramref name="required"/>: the same major (so no breaking API change) and the server is at least
    /// as new (so the mod's expected APIs exist).</summary>
    public static bool Supports(this SemanticVersion server, SemanticVersion required) =>
        server.Major == required.Major && server.CompareTo(required) >= 0;
}
