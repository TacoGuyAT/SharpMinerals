using System.Text.Json;

namespace SharpMinerals.Vanilla.Data;

/// <summary>
/// Loads the vendored minecraft-data block/item tables (embedded as resources, see the project's EmbeddedResource
/// items) and exposes them as the source of truth for content registration and per-protocol wire ids. The 1.20.1
/// (protocol 763) set is the canonical content list registered as blocks/items; both 1.20.1 and 1.19.4 (762)
/// supply the wire ids so a block's state id is correct on each version (they shifted between releases).
/// </summary>
public sealed class MinecraftData {
    /// <summary>The block/item tables for one game version, with name -> wire-id lookups built for fast mapping.</summary>
    public sealed class JavaVersion {
        public required IReadOnlyList<DataBlock> Blocks { get; init; }
        public required IReadOnlyList<DataItem> Items { get; init; }
        /// <summary>Block name (bare, e.g. "stone") -> default wire block-state id.</summary>
        public required IReadOnlyDictionary<string, int> DefaultState { get; init; }
        /// <summary>Item/block name (bare) -> wire item id. Blocks without an item (e.g. water) are absent.</summary>
        public required IReadOnlyDictionary<string, int> ItemId { get; init; }
    }

    /// <summary>1.20.1 / protocol 763 - the canonical content set.</summary>
    public required JavaVersion V763 { get; init; }
    /// <summary>1.19.4 / protocol 762 - wire ids only (a few 1.20 blocks are absent here).</summary>
    public required JavaVersion V762 { get; init; }

    public static MinecraftData Load() => new() {
        V763 = LoadJavaVersion("mcdata.1.20.blocks.json", "mcdata.1.20.items.json"),
        V762 = LoadJavaVersion("mcdata.1.19.4.blocks.json", "mcdata.1.19.4.items.json"),
    };

    static JavaVersion LoadJavaVersion(string blocksResource, string itemsResource) {
        var blocks = Deserialize(blocksResource, MinecraftDataJsonContext.Default.DataBlockArray);
        var items = Deserialize(itemsResource, MinecraftDataJsonContext.Default.DataItemArray);
        return new JavaVersion {
            Blocks = blocks,
            Items = items,
            DefaultState = blocks.ToDictionary(b => b.Name, b => b.DefaultState),
            ItemId = items.ToDictionary(i => i.Name, i => i.Id),
        };
    }

    static T[] Deserialize<T>(string resource, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T[]> typeInfo) {
        using var stream = typeof(MinecraftData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded minecraft-data resource \"{resource}\" not found.");
        return JsonSerializer.Deserialize(stream, typeInfo)
            ?? throw new InvalidOperationException($"minecraft-data resource \"{resource}\" deserialized to null.");
    }
}
