using System.Text.Json.Serialization;

namespace SharpMinerals.Vanilla.Data;

/// <summary>A block entry from minecraft-data's <c>blocks.json</c>. Only the fields the auto-registration uses
/// are modeled; the rest of the (large) JSON is ignored. <see cref="DefaultState"/> is the wire block-state id
/// sent in chunk packets - the whole reason this data is authoritative.</summary>
public sealed record DataBlock {
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int DefaultState { get; init; }
    public bool Diggable { get; init; }

    /// <summary>Mining hardness (seconds*constant). 0 = instant, negative (e.g. bedrock -1) = unbreakable.</summary>
    public float Hardness { get; init; } = -1f;

    /// <summary>Present only for blocks that need a specific tool to harvest (item id -> true); absent = any/hand.</summary>
    public Dictionary<string, bool>? HarvestTools { get; init; }

    /// <summary>Whether a tool is required to harvest (bare hands mine it much slower).</summary>
    [JsonIgnore]
    public bool RequiresTool => HarvestTools is { Count: > 0 };
}

/// <summary>An item entry from minecraft-data's <c>items.json</c>. <see cref="Id"/> is the wire item id.</summary>
public sealed record DataItem {
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int StackSize { get; init; } = 64;
}

/// <summary>Source-generated (de)serialization metadata for the minecraft-data arrays, so the data load works
/// under Native AOT / trimming without reflection (mirrors the pattern in <c>ChatJsonContext</c> /
/// <c>ServerConfigJsonContext</c>). Case-insensitive matching handles the camelCase JSON keys.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DataBlock[]))]
[JsonSerializable(typeof(DataItem[]))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
internal sealed partial class MinecraftDataJsonContext : JsonSerializerContext;
