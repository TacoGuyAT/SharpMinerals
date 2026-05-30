using System.Collections.Concurrent;
using System.Net;

namespace SharpMinerals.Network;

/// <summary>
/// Transport-agnostic surface the rest of the server talks to. Hides whether
/// clients arrive over TCP or anything else.
/// </summary>
public interface INetServer {
    /// <summary>The supported protocol versions; a connection picks one by handshake version.</summary>
    ProtocolRegistry Registry { get; }
    /// <summary>The primary/default protocol (status advertisement, pre-handshake).</summary>
    Protocol Protocol { get; }
    void Start();
    void Stop();
    void Send(ulong client, IMessage message);
    /// <summary>Sends to every client matching <paramref name="predicate"/> (or all if null).</summary>
    void Broadcast(IMessage message, Func<NetClient, bool>? predicate = null);
}

/// <summary>
/// Shared base for concrete transports. Owns the client registry and the active
/// <see cref="Protocol"/>; subclasses implement the listen/accept mechanics.
/// </summary>
public abstract class NetServer<T> : INetServer
    where T : NetClient {
    protected readonly IPEndPoint Endpoint;
    protected readonly ConcurrentDictionary<ulong, T> Clients = new();

    long nextClientId;

    public ProtocolRegistry Registry { get; }
    public Protocol Protocol => Registry.Default;

    protected NetServer(IPEndPoint endpoint, ProtocolRegistry registry) {
        Endpoint = endpoint;
        Registry = registry;
    }

    protected ulong NextClientId() => (ulong)Interlocked.Increment(ref nextClientId);

    protected void Register(T client) => Clients[client.Id] = client;
    protected void Unregister(ulong id) => Clients.TryRemove(id, out _);

    public abstract void Start();
    public abstract void Stop();

    public virtual void Send(ulong client, IMessage message) {
        if (Clients.TryGetValue(client, out var c))
            c.Send(message);
    }

    public virtual void Broadcast(IMessage message, Func<NetClient, bool>? predicate = null) {
        foreach (var c in Clients.Values)
            if (predicate is null || predicate(c))
                c.Send(message);
    }
}
