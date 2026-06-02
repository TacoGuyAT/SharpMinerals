using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Modding;

/// <summary>
/// Host-side helpers for the mod content lifecycle. The host calls <see cref="Freeze"/> after mods'
/// <see cref="Mod.OnInitialize"/> and before the protocols snapshot the palette, so any late registration
/// fails loudly rather than silently desyncing the palette clients were told about.
/// </summary>
public static class ModContent {
    public static void Freeze() {
        BlockRegistry.Freeze();
        ItemRegistry.Freeze();
        EntityRegistry.Freeze();
    }
}
