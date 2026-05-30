using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SharpMinerals.Network.Tcp;

/// <summary>
/// TCP transport. Accepts connections on a background thread and gives each its
/// own receive thread. Decoded packets are routed to the supplied handler.
/// </summary>
public sealed class TcpNetServer : NetServer<TcpNetClient> {
    static readonly ILogger Log = Logging.For("Net.Tcp");

    readonly TcpListener listener;
    readonly Action<NetClient, IMessage> handler;
    readonly Action<NetClient>? onDisconnect;
    volatile bool running;

    public TcpNetServer(IPEndPoint endpoint, ProtocolRegistry registry,
        Action<NetClient, IMessage> handler, Action<NetClient>? onDisconnect = null)
        : base(endpoint, registry) {
        listener = new TcpListener(endpoint);
        this.handler = handler;
        this.onDisconnect = onDisconnect;
    }

    public override void Start() {
        if (running) return;
        running = true;
        listener.Start();
        Log.LogInformation("Listening for connections on {Endpoint}", Endpoint);

        new Thread(AcceptLoop) {
            Name = "TCP Accept Loop",
            IsBackground = true,
        }.Start();
    }

    public override void Stop() {
        running = false;
        try { listener.Stop(); } catch { /* ignore */ }
        foreach (var client in Clients.Values)
            client.Disconnect();
    }

    void AcceptLoop() {
        while (running) {
            TcpClient tcp;
            try {
                tcp = listener.AcceptTcpClient();
            } catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) {
                break; // Listener stopped.
            }

            var client = new TcpNetClient(NextClientId(), tcp, Registry.Default);
            Register(client);

            new Thread(() => {
                try {
                    client.Receive(handler);
                } catch (Exception ex) {
                    // One client's failure must never take down the server.
                    Log.LogWarning(ex, "Client {Client} faulted", client.Id);
                } finally {
                    Unregister(client.Id);
                    client.Disconnect();
                    onDisconnect?.Invoke(client);
                }
            }) {
                Name = $"TCP Client {client.Id}",
                IsBackground = true,
            }.Start();
        }
    }
}
