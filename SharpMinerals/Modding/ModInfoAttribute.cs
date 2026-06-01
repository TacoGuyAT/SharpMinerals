namespace SharpMinerals.Modding;

/// <summary>
/// Assembly-level marker that identifies a mod and carries its metadata: <c>[assembly: ModInfo("my_mod",
/// "1.0.0", new[] { "author" })]</c>. The <see cref="ModLoader"/> scans assemblies for this attribute,
/// then instantiates the single <see cref="Mod"/> subclass the assembly exports. Modelled after
/// HarmonyMine's <c>ModInfoAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ModInfoAttribute : Attribute {
    /// <summary>Stable mod id — alphanumeric plus <c>_ - .</c>. Used for the Harmony id, data dir, and logs.</summary>
    public string ModId { get; }
    public string Version { get; }
    public string[] Authors { get; }
    /// <summary>Optional project/source URL.</summary>
    public string? Url { get; set; }

    public ModInfoAttribute(string modId, string version = "0.0.0", string[]? authors = null) {
        ModId = modId;
        Version = version;
        Authors = authors ?? Array.Empty<string>();
    }
}
