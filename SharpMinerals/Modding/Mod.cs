using HarmonyLib;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace SharpMinerals.Modding;

/// <summary>
/// Base class for a SharpMinerals mod. A mod assembly <see cref="Mod"/> subclasses with
/// <see cref="ModInfoAttribute"/>; the <see cref="ModLoader"/> instantiates it and drives
/// its lifecycle.
/// <para/>
/// Lifecycle, in order:
/// <list type="number">
/// <item><see cref="OnInitialize"/> — register content (blocks/items/entities) and apply Harmony patches.
/// Runs BEFORE the protocols/type-mappers and the <c>Server</c> are built, so registered content is in the
/// palette. There is no server yet here.</item>
/// <item><see cref="OnServerStarted"/> — the server exists and is running: register commands on its
/// <c>CommandDispatcher</c>, subscribe to events, adjust the MOTD, spawn things.</item>
/// <item><see cref="OnServerStopping"/> — the server is shutting down: release anything the mod owns.</item>
/// </list>
/// </summary>
public abstract class Mod {
    /// <summary>This mod's metadata, from its assembly's <see cref="ModInfoAttribute"/>.</summary>
    public ModInfoAttribute Info { get; internal set; } = null!;

    /// <summary>This mod's semantic version, parsed from <see cref="ModInfoAttribute.Version"/> (the loader
    /// validates it before the mod loads).</summary>
    public SemanticVersion Version { get; private set; } = null!;

    /// <summary>A Harmony instance scoped to this mod (id = <see cref="ModInfoAttribute.ModId"/>). Call
    /// <c>Harmony.PatchAll()</c> from <see cref="OnInitialize"/> to apply the mod's <c>[HarmonyPatch]</c> classes.</summary>
    public Harmony Harmony { get; internal set; } = null!;

    /// <summary>A writable per-mod data directory (created for you), for config/state files.</summary>
    public string DataPath { get; internal set; } = null!;

    /// <summary>A logger categorised by the mod id, bound to the host's logging backend.</summary>
    protected ILogger Logger { get; private set; } = null!;

    internal void Bind(ModInfoAttribute info, Harmony harmony, string dataPath) {
        Info = info;
        Harmony = harmony;
        DataPath = dataPath;
        Version = SemanticVersion.TryParse(info.Version, out var v) ? v : new SemanticVersion(0, 0, 0); // validated by the loader
        Logger = Logging.For($"Mod/{info.ModId}");
    }

    /// <summary>Register content and apply patches. Runs before the server (and protocols) are built.</summary>
    public virtual void OnInitialize() { }

    /// <summary>The server has started. Register commands, subscribe to events, adjust runtime state.</summary>
    public virtual void OnServerStarted(Server server) { }

    /// <summary>The server is shutting down. Release mod-owned resources.</summary>
    public virtual void OnServerStopping(Server server) { }
}
