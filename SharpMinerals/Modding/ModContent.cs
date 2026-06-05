using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Modding;

/// <summary>
/// Host-side helpers for the mod content lifecycle. The host calls <see cref="Freeze"/> after mods'
/// <see cref="Mod.OnInitialize"/> and before the protocols snapshot the palette, so any late registration
/// fails loudly rather than silently desyncing the palette clients were told about.
/// </summary>
public static class ModContent {
    /// <summary>The namespace assigned to new registry entries. Defaults to <c>minecraft</c> (built-ins); the
    /// <see cref="ModLoader"/> sets it to the loading mod's id around its <see cref="Mod.OnInitialize"/>, so a
    /// mod's content is namespaced under its id without the mod repeating it.</summary>
    internal static string CurrentNamespace { get; set; } = "minecraft";

    public static void Freeze() {
        BlockRegistry.Freeze();
        ItemRegistry.Freeze();
        EntityRegistry.Freeze();
        ComponentRegistry.Freeze();
    }
}
