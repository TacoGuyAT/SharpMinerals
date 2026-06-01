using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Modding;

/// <summary>
/// Host-side helpers for the mod content lifecycle. After mods have run their <see cref="Mod.OnInitialize"/>
/// (where they register blocks/items/entities) and before the protocols/type-mappers snapshot the palette,
/// the host calls <see cref="Freeze"/> to seal every registry — so any late registration fails loudly
/// rather than silently desyncing the palette the clients were told about.
/// </summary>
public static class ModContent {
    /// <summary>Seals the block, item, and entity registries against further registration.</summary>
    public static void Freeze() {
        BlockRegistry.Freeze();
        ItemRegistry.Freeze();
        EntityRegistry.Freeze();
    }
}
