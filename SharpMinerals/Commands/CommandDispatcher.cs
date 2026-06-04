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
/// Owns command parsing and dispatch over a Brigadier.NET dispatcher with <see cref="SenderContext"/> as the
/// source. <see cref="ExecuteAsync"/> is the entry point for a submitted line; <see cref="Register"/> adds
/// commands via Brigadier builder lambdas.
/// </summary>
public sealed class CommandDispatcher {
    static readonly ILogger Log = Logging.For<CommandDispatcher>();

    readonly CommandDispatcher<SenderContext> brig = new();

    // Parsing is the costly step and a ParseResults can be re-executed, so we cache it. A parse binds its
    // source's .Requires pruning, so the key embeds a global generation + per-player epoch; bumping either
    // orphans affected entries, which lapse via the TTL (FastCache has no prefix scan to evict directly).
    readonly FastCache<string, ParseResults<SenderContext>> parseCache = new();
    static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    // FastCache is unbounded, so we cap it ourselves and scale the cap with player count (each player has its
    // own id+epoch key namespace). The base allowance covers the console plus headroom.
    const int BaseCacheEntries = 256;
    const int CacheEntriesPerPlayer = 64;
    int CacheCapacity => BaseCacheEntries + CacheEntriesPerPlayer * Server.PlayerCount;

    // Bumped on a Register (tree change); invalidates every cached parse.
    int generation;
    // Per-player parse generation, bumped by Invalidate; absent (== 0) for a never-invalidated player.
    readonly ConcurrentDictionary<ulong, int> playerEpoch = new();

    public Server Server { get; set; }

    /// <summary>The underlying Brigadier dispatcher (for serializing the command tree and answering
    /// suggestions).</summary>
    public CommandDispatcher<SenderContext> Brigadier => brig;

    public CommandDispatcher(Server server) {
        Server = server;
    }

    /// <summary>Registers a command from a Brigadier builder lambda; chainable. Invalidates every cached
    /// parse.</summary>
    public CommandDispatcher Register(Func<IArgumentContext<SenderContext>, LiteralArgumentBuilder<SenderContext>> command) {
        brig.Register(command);
        Interlocked.Increment(ref generation);
        return this;
    }

    /// <summary>Invalidates a player's cached parses when their <c>.Requires</c> inputs change (permissions,
    /// world). The next run re-parses against current state.</summary>
    public void Invalidate(ulong clientId) => playerEpoch.AddOrUpdate(clientId, 1, static (_, epoch) => epoch + 1);

    /// <summary>Drops a disconnected player's epoch entry; their cached parses lapse via the TTL. Client ids
    /// are monotonic, so the entry can't be reused by a later connection.</summary>
    public void Forget(ulong clientId) => playerEpoch.TryRemove(clientId, out _);

    /// <summary>Runs a command line (no leading slash) as <paramref name="sender"/>. <paramref name="client"/>
    /// is the issuing player's connection (null for the console), giving player-perspective commands their
    /// entity. The returned task completes synchronously.</summary>
    public Task ExecuteAsync(ISender sender, string text, NetClient? client = null) {
        text = text.Trim();
        if (text.Length == 0)
            return Task.CompletedTask;

        string key = CacheKey(client, text);
        if (!parseCache.TryGet(key, out var parse)) {
            parse = brig.Parse(text, new SenderContext(sender, this, client));
            // At the cap, reclaim lapsed entries; still over it, skip memoizing (a re-parse is cheap).
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
    /// Tab-completion suggestions for a partial command line as <paramref name="sender"/>, returned as the
    /// replace range plus the matches. <paramref name="client"/> gives player-perspective commands their
    /// entity. Synchronous.
    /// </summary>
    public (int Start, int Length, IReadOnlyList<string> Matches) Suggest(ISender sender, string text, NetClient? client = null) {
        // The vanilla client sends the whole input including the leading '/', so skip it for parsing but keep
        // the returned Start in the client's full-input coordinates - else it replaces the wrong span.
        int offset = 0;
        if (text.StartsWith('/')) { text = text[1..]; offset = 1; }
        var suggestions = brig
            .GetCompletionSuggestions(brig.Parse(text, new SenderContext(sender, this, client)))
            .GetAwaiter().GetResult();
        var matches = new List<string>(suggestions.List.Count);
        foreach (var suggestion in suggestions.List) matches.Add(suggestion.Text);
        return (suggestions.Range.Start + offset, suggestions.Range.Length, matches);
    }

    // generation + source identity (+ per-player epoch) + text. The leading fields are spaceless, so spaces in
    // the text can't make two distinct tuples collide.
    string CacheKey(NetClient? client, string text) {
        int gen = Volatile.Read(ref generation);
        return client is null
            ? $"{gen} console {text}"
            : $"{gen} {client.Id} {playerEpoch.GetValueOrDefault(client.Id)} {text}";
    }
}
