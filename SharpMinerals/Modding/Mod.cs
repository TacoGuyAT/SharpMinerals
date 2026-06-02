using HarmonyLib;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace SharpMinerals.Modding;

/// <summary>
/// Base class for a SharpMinerals mod, subclassed with <see cref="ModInfoAttribute"/> and driven by the
/// <see cref="ModLoader"/>. Lifecycle, in order:
/// <list type="number">
/// <item><see cref="OnInitialize"/> — register content and apply Harmony patches; runs before the server and
/// protocols are built.</item>
/// <item><see cref="OnServerStarted"/> — server is running: register commands, subscribe to events, etc.</item>
/// <item><see cref="OnServerStopping"/> — server is shutting down: release mod-owned resources.</item>
/// </list>
/// </summary>
public abstract class Mod {
    /// <summary>This mod's metadata, from its assembly's <see cref="ModInfoAttribute"/>.</summary>
    public ModInfoAttribute Info { get; internal set; } = null!;

    public SemanticVersion Version { get; private set; } = null!;

    /// <summary>A Harmony instance scoped to this mod (id = <see cref="ModInfoAttribute.ModId"/>). Call
    /// <c>PatchAll()</c> from <see cref="OnInitialize"/> to apply the mod's patches.</summary>
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
