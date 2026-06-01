namespace SharpMinerals.Entities.Components;

/// <summary>
/// Per-player record of which chunk columns its client currently has loaded, so the server can
/// stream new columns (and forget out-of-range ones) as the player moves. A reference-type ECS
/// component; transient view state — never persisted.
/// </summary>
public sealed class ChunkViewEntityComponent {
    /// <summary>The chunk column the player is currently centred on.</summary>
    public long CenterX;
    public long CenterZ;

    /// <summary>False until the first view has been streamed (so a fresh player always streams).</summary>
    public bool Initialized;

    /// <summary>Columns the client is believed to currently hold.</summary>
    public readonly HashSet<(long X, long Z)> Loaded = new();
}
