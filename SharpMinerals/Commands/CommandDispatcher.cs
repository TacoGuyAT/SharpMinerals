using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Exceptions;
using Jitbit.Utils;
using SharpMinerals.Chat;
using SharpMinerals.Network;

namespace SharpMinerals.Commands;

/// <summary>
/// Owns command parsing and dispatch, wrapping a Brigadier.NET dispatcher over <see cref="SenderContext"/>
/// as the command source. <see cref="ExecuteAsync"/> is the single entry point for a submitted command line
/// (no leading slash); <see cref="Register"/> adds commands via Brigadier builder lambdas.
/// </summary>
public sealed class CommandDispatcher {
    static readonly ILogger Log = Logging.For("CommandDispatcher");

    readonly CommandDispatcher<SenderContext> brig = new();

    // The parse step is the costly one, and a ParseResults can be re-executed (per the Brigadier docs), so we
    // cache it. A parse binds its source and its .Requires pruning, so the cache must be invalidated when that
    // pruning could change. Rather than enumerate-and-evict (FastCache has no prefix scan), the key embeds a
    // global generation plus a per-player epoch; bumping either orphans the affected entries, which then lapse
    // via the TTL. So a re-register (tree changed) and a player's permission/world change can't be bypassed.
    // (The entity itself is resolved live at execute time, see CommandContext, so the cache is already correct
    // across respawns/world switches even before any world-gated .Requires exists.)
    readonly FastCache<string, ParseResults<SenderContext>> parseCache = new();
    static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    // FastCache has no built-in capacity, so we bound it ourselves AND scale the bound with the player count:
    // each connected player has their own key namespace (id + epoch), so the working set grows with players.
    // The base allowance covers the console plus headroom. (The Phase 3 suggestion cache will size itself the
    // same way via CacheCapacity.)
    const int BaseCacheEntries = 256;
    const int CacheEntriesPerPlayer = 64;
    int CacheCapacity => BaseCacheEntries + CacheEntriesPerPlayer * Server.PlayerCount;

    // Bumped when the command tree changes (a Register) - invalidates every cached parse, console included.
    int generation;
    // Per-player parse generation, bumped by Invalidate; absent (== 0) for a player never invalidated.
    readonly ConcurrentDictionary<ulong, int> playerEpoch = new();

    /// <summary>The server this dispatcher drives, injected by <see cref="Server"/>. Commands reach it through
    /// <see cref="SenderContext.Server"/> instead of a global static.</summary>
    public Server Server { get; set; }

    /// <summary>The underlying Brigadier dispatcher, exposed so a later phase can serialize the command tree
    /// (the Declare Commands packet) and answer completion suggestions.</summary>
    public CommandDispatcher<SenderContext> Brigadier => brig;

    public CommandDispatcher(Server server) {
        Server = server;
    }

    /// <summary>Registers a command from a Brigadier builder lambda; chainable. Changing the tree invalidates
    /// every cached parse (their <c>.Requires</c> pruning may no longer hold).</summary>
    public CommandDispatcher Register(Func<IArgumentContext<SenderContext>, LiteralArgumentBuilder<SenderContext>> command) {
        brig.Register(command);
        Interlocked.Increment(ref generation);
        return this;
    }

    /// <summary>Invalidates a player's cached parses, for when anything their <c>.Requires</c> predicates depend
    /// on changes (permissions, the world/dimension they are in). The next run re-parses against current state.</summary>
    public void Invalidate(ulong clientId) => playerEpoch.AddOrUpdate(clientId, 1, static (_, epoch) => epoch + 1);

    /// <summary>Drops a disconnected player's epoch entry (their cached parses lapse via the TTL). Client ids
    /// are monotonic, so the entry cannot be wrongly reused by a later connection.</summary>
    public void Forget(ulong clientId) => playerEpoch.TryRemove(clientId, out _);

    /// <summary>Runs a command line (no leading slash) as <paramref name="sender"/>. <paramref name="client"/>
    /// is the issuing player's connection (null for the console), which gives player-perspective commands their
    /// entity. The returned task completes synchronously: Brigadier executes synchronously, and the few commands
    /// that need concurrency arrange it themselves.</summary>
    public Task ExecuteAsync(ISender sender, string text, NetClient? client = null) {
        text = text.Trim();
        if (text.Length == 0)
            return Task.CompletedTask;

        string key = CacheKey(client, text);
        if (!parseCache.TryGet(key, out var parse)) {
            parse = brig.Parse(text, new SenderContext(sender, this, client));
            // Reclaim lapsed/orphaned entries when at the cap; past it, run the parse but don't memoize it
            // (parse is cheap, so an over-cap miss just costs a re-parse — memory stays bounded).
            int cap = CacheCapacity;
            if (parseCache.Count >= cap)
                parseCache.EvictExpired();
            if (parseCache.Count < cap)
                parseCache.AddOrUpdate(key, parse, CacheTtl);
        }

        try {
            brig.Execute(parse);
        } catch (CommandSyntaxException ex) {
            // User-facing: unknown command, bad/missing argument, or a failed .Requires.
            sender.ReceiveMessage(new TextComponent(ex.Message));
            Log.LogDebug(ex, "command syntax error: {Text}", text);
        } catch (Exception ex) {
            sender.ReceiveMessage(new TextComponent($"Error: {ex.Message}"));
            Log.LogWarning(ex, "command '{Text}' failed", text);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Computes tab-completion suggestions for a partial command line as <paramref name="sender"/>
    /// (<paramref name="client"/> gives player-perspective commands their entity / passes <c>.Requires</c>).
    /// The vanilla client sends the WHOLE chat input INCLUDING the leading <c>/</c>, so we skip it for parsing
    /// but keep the returned <c>Start</c> in the client's full-input coordinates (shifted back by the skipped
    /// slash) — otherwise the client replaces the wrong span and the suggestion popup shows nothing. Returns
    /// the replace range and the matches. Synchronous: our suggestion providers complete inline.
    /// </summary>
    public (int Start, int Length, IReadOnlyList<string> Matches) Suggest(ISender sender, string text, NetClient? client = null) {
        int offset = 0;
        if (text.StartsWith('/')) { text = text[1..]; offset = 1; }
        var suggestions = brig
            .GetCompletionSuggestions(brig.Parse(text, new SenderContext(sender, this, client)))
            .GetAwaiter().GetResult();
        var matches = new List<string>(suggestions.List.Count);
        foreach (var suggestion in suggestions.List) matches.Add(suggestion.Text);
        return (suggestions.Range.Start + offset, suggestions.Range.Length, matches);
    }

    // generation + source identity (+ per-player epoch) + text. Bumping generation or the player's epoch changes
    // the key, so stale parses are never hit again and lapse via the TTL. Unambiguous despite spaces in the text:
    // the leading fields are spaceless integers (or the literal "console"), so two distinct tuples never collide.
    string CacheKey(NetClient? client, string text) {
        int gen = Volatile.Read(ref generation);
        return client is null
            ? $"{gen} console {text}"
            : $"{gen} {client.Id} {playerEpoch.GetValueOrDefault(client.Id)} {text}";
    }
}
