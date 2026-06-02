namespace SharpMinerals.Modding;

/// <summary>Marks a <see cref="Mod"/> subclass and declares its metadata for the <see cref="ModLoader"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ModInfoAttribute : Attribute {
    /// <summary>Unique mod identifier (letters, digits, and _ only).</summary>
    public string ModId { get; }
    public string Version { get; }
    public string[] Authors { get; }
    public string? Url { get; set; }

    /// <summary>Server version this mod targets; null for version-agnostic mods.</summary>
    public string? TargetServerVersion { get; set; }

    public ModInfoAttribute(string modId, string version = "0.0.0", string[]? authors = null) {
        ModId = modId;
        Version = version;
        Authors = authors ?? Array.Empty<string>();
    }
}
