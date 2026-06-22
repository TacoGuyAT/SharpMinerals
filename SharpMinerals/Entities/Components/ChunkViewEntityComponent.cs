namespace SharpMinerals.Entities.Components;

/// <summary>Per-player record of which chunk columns its client currently has loaded, so the server can
/// stream new columns (and forget out-of-range ones) as the player moves. Transient - never persisted.</summary>
[Component]
public sealed class ChunkViewEntityComponent {
    /// <summary>The chunk column the player is currently centred on.</summary>
    public Mint CenterX;
    public Mint CenterZ;

    /// <summary>False until the first view has been streamed (so a fresh player always streams).</summary>
    public bool Initialized;

    /// <summary>True once every column in view has been streamed, so a stationary player does no per-tick work.
    /// Cleared whenever the player crosses a column (new frontier) or a column is still awaiting background
    /// generation, so the streamer keeps retrying owed columns until the whole view is sent.</summary>
    public bool Settled;

    /// <summary>Columns the client is believed to currently hold.</summary>
    public readonly HashSet<(Mint X, Mint Z)> Loaded = [];
}
