using SharpMinerals.Blocks;
using SharpMinerals.Items.Components;
using System.Diagnostics.CodeAnalysis;

namespace SharpMinerals.Items;

/// <summary>A registered item type - a flyweight definition (one shared instance per kind) assembled from
/// components. Everything beyond identity (stack size, placement, ...) lives in components.
/// <see cref="BlockType"/> derives from this - every block is also an item.</summary>
public class ItemType : ComponentObject {
    public static IReadOnlyList<ItemType> All => Registry.All;
    public static readonly Registry<ItemType> Registry = new();
    public static ItemType Register(string name, int maxStackSize = 64)
        => Registry.Register(name, (id, identifier) => new ItemType(id, identifier).Add(new Stackable(maxStackSize)));
    public static bool TryFromPath(string path, [MaybeNullWhen(false)] out ItemType result) => Registry.TryFromPath(path, out result);

    internal int ItemId { get; set; }

    /// <summary>The namespaced identifier (e.g. <c>minecraft:stone</c>): <c>Id.Namespace</c> + <c>Id.Name</c>,
    /// with the cached <c>Id.ToString()</c> full string used for registry lookups, persistence, and wire mapping.</summary>
    public Identifier Id { get; }

    internal ItemType(int id, Identifier identifier) {
        ItemId = id;
        Id = identifier;
    }

    /// <summary>Override for <see cref="IsCustom"/>; null = the namespace-based default. Set via <c>.Custom(bool)</c>.</summary>
    internal bool? CustomOverride { get; set; }

    /// <summary>Whether the client needs a custom name + identity marker for this item - i.e. it is NOT native
    /// vanilla content (so it shows its real name and doesn't stack with the vanilla item it renders as). Auto:
    /// non-<c>minecraft</c> content (engine placeholders like <c>missing</c>, mod items) is custom, <c>minecraft</c>
    /// content is native. Override per-type with the fluent <see cref="ItemTypes.Custom{T}"/>.</summary>
    public bool IsCustom => CustomOverride ?? Id.Namespace != Identifier.MinecraftNamespace;

    /// <summary>Max stack size (from a <see cref="Stackable"/> component, default 64).</summary>
    public int MaxStackSize => TryGet<Stackable>(out var s) ? s.MaxStackSize : 64;

    /// <summary>The block this item places when used, if any (from a <see cref="Placeable"/> component).</summary>
    public virtual BlockType? PlacedBlock => TryGet<Placeable>(out var p) ? p.Block : null;

    public override string ToString() => Id.Name;
}

/// <summary>Fluent helpers for item/block types.</summary>
public static class ItemTypes {
    /// <summary>Overrides <see cref="ItemType.IsCustom"/> (vs the namespace-based default) - e.g. a mod item that
    /// should render as a plain vanilla item (<c>custom: false</c>), or vanilla content shown distinctly.</summary>
    public static T Custom<T>(this T self, bool custom = true) where T : ItemType {
        self.CustomOverride = custom;
        return self;
    }
}
