using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace SharpMinerals.Modding;

/// <summary>
/// Discovers and drives mods. A host (e.g. SharpMinerals.CLI) creates one, feeds it mods — either as
/// already-loaded assemblies (<see cref="LoadFrom"/>, for compiled-in / consuming-assembly mods) or by
/// scanning a directory of <c>*.dll</c> files (<see cref="LoadDirectory"/>) — then runs the lifecycle:
/// loading calls each mod's <see cref="Mod.OnInitialize"/> (content registration), after which the host
/// freezes the registries (<see cref="ModContent.Freeze"/>) and builds the protocols + server, then calls
/// <see cref="StartAll"/> / <see cref="StopAll"/>. Modelled after HarmonyMine's <c>Mod.TryRegister</c> flow.
/// </summary>
public sealed partial class ModLoader {
    [GeneratedRegex(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex ModIdRegex();
    static readonly Regex ModIdPattern = ModIdRegex();

    readonly ILogger log = Logging.For<ModLoader>();
    readonly List<Mod> mods = [];
    readonly HashSet<string> ids = [];

    /// <summary>The mods loaded so far, in load order.</summary>
    public IReadOnlyList<Mod> Mods => mods;

    /// <summary>The running server's version, used to gate mods by their declared <see
    /// cref="ModInfoAttribute.TargetServerVersion"/>. Defaults to the SharpMinerals assembly version; a
    /// host or test may override it.</summary>
    public SemanticVersion ServerVersion { get; set; } = CoreVersion();

    static SemanticVersion CoreVersion() {
        var v = typeof(ModLoader).Assembly.GetName().Version;
        return v is null ? new SemanticVersion(0, 0, 0) : new SemanticVersion(v.Major, v.Minor, v.Build);
    }

    /// <summary>Loads mods from assemblies already in the process (a host's own referenced mod projects).</summary>
    public void LoadFrom(params Assembly[] assemblies) {
        foreach (var assembly in assemblies)
            TryLoad(assembly);
    }

    public bool TryLoad(Mod mod, ModInfoAttribute info) {
        if(!ModIdPattern.IsMatch(info.ModId)) {
            log.LogError("Mod id \"{Id}\" is invalid — use letters, digits, and _ - . only.", info.ModId);
            return false;
        }
        if(!SemanticVersion.TryParse(info.Version, out _)) {
            log.LogError("Mod \"{Id}\" has an invalid semantic version \"{Version}\".", info.ModId, info.Version);
            return false;
        }
        // Gate on the declared target server version: same major (no breaking API change) and the running
        // server at least as new (so the mod's expected APIs exist). No target = no check.
        if(info.TargetServerVersion is { } targetText) {
            if(!SemanticVersion.TryParse(targetText, out var target)) {
                log.LogError("Mod \"{Id}\" declares an invalid target server version \"{Target}\".", info.ModId, targetText);
                return false;
            }
            if(!ServerVersion.Supports(target)) {
                log.LogWarning("Mod \"{Id}\" targets server {Target}, but this server is {Server} — skipping (incompatible).",
                    info.ModId, target, ServerVersion);
                return false;
            }
        }
        if(!ids.Add(info.ModId)) {
            log.LogError("Duplicate mod id \"{Id}\" ({Assembly}) — skipping.", info.ModId, mod.GetType().Assembly);
            return false;
        }

        var dataPath = Path.Combine("mods", "data", info.ModId);
        Directory.CreateDirectory(dataPath);
        mod.Bind(info, new Harmony(info.ModId), dataPath);

        // TODO: better handling
        try {
            mod.OnInitialize();
        } catch(Exception ex) {
            log.LogError(ex, "Mod \"{Id}\" OnInitialize threw — the mod may be partially loaded.", info.ModId);
            // It's already past content registration; keep it so OnServerStarted still runs, matching
            // a "best effort" load. A throwing mod logs loudly rather than aborting the whole server.
        }

        mods.Add(mod);
        return true;
    }

    public bool TryLoad<T>(T mod) where T : Mod, new() {
        if(typeof(T).GetCustomAttribute<ModInfoAttribute>() is not ModInfoAttribute attr) {
            return false;
        }
        return TryLoad(new T(), attr);
    }

    /// <summary>Loads mods from every <c>*.dll</c> directly in <paramref name="directory"/> (created if absent).
    /// Each assembly that carries a <see cref="ModInfoAttribute"/> is treated as a mod; others are ignored.</summary>
    public void LoadDirectory(string directory) {
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)) {
            try {
                TryLoad(Assembly.LoadFrom(Path.GetFullPath(file)));
            } catch (Exception ex) {
                log.LogError(ex, "Failed to load mod assembly {File}", file);
            }
        }
    }

    bool TryLoad(Assembly assembly) {
        // TODO: Figure out compiler warnings
        var candidates = assembly.GetExportedTypes()
            .Select(mod => {
                if(mod.IsSubclassOf(typeof(Mod)) && !mod.IsAbstract && mod.GetCustomAttribute<ModInfoAttribute>() is ModInfoAttribute info) {
                    return (mod, info);
                }
                return (mod, null);
            })
            .Where(x => x.mod is not null && x.info is not null)
            .ToList();

        var count = 0;
        foreach(var (m, info) in candidates) {
            count++;
            if(TryLoad((Mod)Activator.CreateInstance(m)!, info)) {
                log.LogInformation("Loaded mod \"{Id}\" v{Version}{Authors}", info.ModId, info.Version,
                    info.Authors.Length > 0 ? $" by {string.Join(", ", info.Authors)}" : "");
            }
        }

        if(count == 0) {
            log.LogDebug("Assembly \"{Assembly}\" exports no public Mod subclass.", assembly);
        }

        return true;
    }

    /// <summary>Calls <see cref="Mod.OnServerStarted"/> on every loaded mod (after the server has started).</summary>
    public void StartAll(Server server) => ForEach(server, (m, s) => m.OnServerStarted(s), "OnServerStarted");

    /// <summary>Calls <see cref="Mod.OnServerStopping"/> on every loaded mod (during shutdown).</summary>
    public void StopAll(Server server) => ForEach(server, (m, s) => m.OnServerStopping(s), "OnServerStopping");

    void ForEach(Server server, Action<Mod, Server> action, string phase) {
        foreach (var mod in mods) {
            try {
                action(mod, server);
            } catch (Exception ex) {
                log.LogError(ex, "Mod \"{Id}\" {Phase} threw.", mod.Info.ModId, phase);
            }
        }
    }
}
