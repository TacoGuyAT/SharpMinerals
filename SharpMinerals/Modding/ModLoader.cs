using System.Reflection;
using System.Text.RegularExpressions;
#if !AOT
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
#endif
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace SharpMinerals.Modding;

/// <summary>
/// Discovers and drives mods. A host feeds it mods — already-loaded assemblies (<see cref="LoadFrom"/>) or a
/// directory of <c>*.dll</c> files (<see cref="LoadDirectory"/>) — which calls each mod's
/// <see cref="Mod.OnInitialize"/>; the host then freezes the registries and builds the server, and later calls
/// <see cref="StartAll"/> / <see cref="StopAll"/>.
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

    /// <summary>The running server's version, used to gate mods by their declared
    /// <see cref="ModInfoAttribute.TargetServerVersion"/>. Defaults to the core assembly version; overridable.</summary>
    public SemanticVersion ServerVersion { get; set; } = CoreVersion();

    static SemanticVersion CoreVersion() {
        var v = typeof(ModLoader).Assembly.GetName().Version;
        return v is null ? new SemanticVersion(0, 0, 0) : new SemanticVersion(v.Major, v.Minor, v.Build);
    }

#if !AOT
    // Reflection over assembly types — the mod-discovery boundary. Declared (not suppressed) so the requirement
    // propagates to hosts. Compiled out of AOT builds entirely (the AOT symbol); use the type-safe TryLoad<T>
    // for compiled-in mods there.
    const string DynamicModLoading = "Discovers mods by scanning assembly types via reflection and instantiating "
        + "them; types may be trimmed and external assemblies can't be loaded under Native AOT. Use TryLoad<T> "
        + "for compiled-in mods.";

    /// <summary>Loads mods from assemblies already in the process (a host's own referenced mod projects).</summary>
    [RequiresUnreferencedCode(DynamicModLoading)]
    [RequiresDynamicCode(DynamicModLoading)]
    public void LoadFrom(params Assembly[] assemblies) {
        foreach (var assembly in assemblies)
            TryLoad(assembly);
    }
#endif

    public bool TryLoad(Mod mod, ModInfoAttribute info) {
        if(!ModIdPattern.IsMatch(info.ModId)) {
            log.LogError("Mod id \"{Id}\" is invalid — use letters, digits, and _ - . only.", info.ModId);
            return false;
        }
        if(!SemanticVersion.TryParse(info.Version, out _)) {
            log.LogError("Mod \"{Id}\" has an invalid semantic version \"{Version}\".", info.ModId, info.Version);
            return false;
        }
        // Gate on the declared target server version (see ModVersioning.Supports). No target = no check.
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
        mod.Bind(info,
#if !AOT
                 new Harmony(info.ModId),
#endif
                 dataPath);

        // TODO: better handling
        try {
            mod.OnInitialize();
        } catch(Exception ex) {
            log.LogError(ex, "Mod \"{Id}\" OnInitialize threw — the mod may be partially loaded.", info.ModId);
            // Best-effort: keep the mod so OnServerStarted still runs, rather than aborting the whole server.
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

#if !AOT
    /// <summary>Loads mods from every <c>*.dll</c> directly in <paramref name="directory"/> (created if absent).
    /// Each assembly that carries a <see cref="ModInfoAttribute"/> is treated as a mod; others are ignored.</summary>
    [RequiresUnreferencedCode(DynamicModLoading)]
    [RequiresDynamicCode(DynamicModLoading)]
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

    [RequiresUnreferencedCode(DynamicModLoading)]
    [RequiresDynamicCode(DynamicModLoading)]
    bool TryLoad(Assembly assembly) {
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
#endif

    public void StartAll(Server server) => ForEach(server, (m, s) => m.OnServerStarted(s), "OnServerStarted");

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
