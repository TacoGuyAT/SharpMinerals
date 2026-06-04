using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals;

/// <summary>
/// A component declaring which vanilla content a modded item/block/entity emulates on the wire - the mod's
/// registration point for protocol mapping. A type mapper resolves a definition to <see cref="Vanilla"/> and
/// looks that up in its per-version table, instead of always falling back to a placeholder. Built-in
/// (<c>minecraft</c>-namespace) content needs none - its own <see cref="Identifier"/> is the vanilla id.
/// </summary>
public sealed class VanillaMapping {
    /// <summary>The vanilla identifier this definition maps to (e.g. <c>minecraft:stone</c>).</summary>
    public Identifier Vanilla { get; }

    /// <summary>Maps to an existing item or block type (every block is an item).</summary>
    public VanillaMapping(ItemType type) => Vanilla = type.Id;

    /// <summary>Maps to an existing entity type.</summary>
    public VanillaMapping(EntityType type) => Vanilla = type.Id;

    /// <summary>The vanilla identifier <paramref name="definition"/> maps to: its own id when it's built-in
    /// content, its declared <see cref="VanillaMapping"/> when modded, or its own (unmapped) id otherwise - a
    /// caller's per-version table then resolves it, falling back when the namespace isn't <c>minecraft</c>.</summary>
    public static Identifier TargetOf(Identifier own, ComponentObject definition) =>
        own.Namespace == Identifier.MinecraftNamespace ? own
        : definition.TryGet<VanillaMapping>(out var mapping) ? mapping.Vanilla
        : own;
}

/// <summary>Fluent helpers for borrowing a wire mapping from another definition.</summary>
public static class VanillaMappingExtensions {
    /// <summary>Makes <paramref name="self"/> map to the same vanilla content as <paramref name="source"/> on the
    /// wire - copies <paramref name="source"/>'s <see cref="VanillaMapping"/> if it has one, or points at
    /// <paramref name="source"/> itself when it's built-in (which carries no mapping component).</summary>
    public static T CopyMapping<T>(this T self, ItemType source) where T : ComponentObject {
        self.Set(source.TryGet<VanillaMapping>(out var mapping) ? mapping : new VanillaMapping(source));
        return self;
    }

    /// <inheritdoc cref="CopyMapping{T}(T, ItemType)"/>
    public static T CopyMapping<T>(this T self, EntityType source) where T : ComponentObject {
        self.Set(source.TryGet<VanillaMapping>(out var mapping) ? mapping : new VanillaMapping(source));
        return self;
    }
}
