namespace SharpMinerals.Modding;

/// <summary>
/// TODO: Replace with example and guidelines instead of implementation description.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ModInfoAttribute : Attribute {
    /// <summary>Alphanumeric plus _ mod identifier string.</summary>
    public string ModId { get; }
    public string Version { get; }
    public string[] Authors { get; }
    public string? Url { get; set; }

    /// <summary>Null for version-agnostic mods.</summary>    
    public string? TargetServerVersion { get; set; }

    public ModInfoAttribute(string modId, string version = "0.0.0", string[]? authors = null) {
        ModId = modId;
        Version = version;
        Authors = authors ?? Array.Empty<string>();
    }
}
