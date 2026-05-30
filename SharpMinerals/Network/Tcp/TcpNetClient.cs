using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network.Tcp;

/// <summary>
/// A client connected over TCP. Wraps the socket in a <see cref="MinecraftStream"/>
/// and runs a blocking read loop on its own thread, decoding each framed packet
/// with the active protocol and forwarding it to the server's handler.
/// </summary>
public sealed class TcpNetClient : NetClient {
    static readonly ILogger Log = Logging.For("Net.Tcp");

    readonly TcpClient tcp;
    readonly MinecraftStream stream;
    readonly object writeLock = new();

    public TcpNetClient(ulong id, TcpClient tcp, Protocol protocol) : base(id, protocol) {
        this.tcp = tcp;
        stream = new MinecraftStream(tcp.GetStream());
    }

    public override void Send(IMessage message) {
        // Multiple threads (the read loop and the tick loop's broadcasts) may write,
        // so serialize access to the socket.
        int bytes;
        lock (writeLock)
            bytes = Packets.Write(stream, Protocol, message);

        Log.LogDebug("→ #{Client} [{State}] {Packet} ({Bytes} B)",
            Id, State, message.GetType().Name, bytes);
    }

    public override void Disconnect() {
        try { tcp.Close(); } catch { /* already gone */ }
    }

    /// <summary>
    /// Blocking receive loop. Decodes packets against the connection's current
    /// <see cref="NetClient.State"/> and hands each message to <paramref name="onMessage"/>.
    /// Returns when the peer disconnects or the socket faults.
    /// </summary>
    public void Receive(Action<NetClient, IMessage> onMessage) {
        try {
            while (tcp.Connected) {
                var frame = Packets.Read(stream);
                var codec = Protocol.CodecFor(State, PacketDirection.Serverbound, frame.Id);
                if (codec is null) {
                    Log.LogDebug("← #{Client} [{State}] unknown packet 0x{Id:X2} — ignored", Id, State, frame.Id);
                    continue; // Unknown/unsupported packet for this state.
                }

                var message = codec.Decode(frame.Payload);
                Log.LogDebug("← #{Client} [{State}] {Packet} (0x{Id:X2})", Id, State, message.GetType().Name, frame.Id);
                onMessage(this, message);
            }
        } catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException or InvalidOperationException) {
            // Normal disconnects and socket teardown.
            Log.LogDebug("← #{Client} disconnected ({Reason})", Id, ex.GetType().Name);
        }
    }
}
