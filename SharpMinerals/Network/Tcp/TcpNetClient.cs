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
    readonly ProtocolRegistry registry;
    readonly object writeLock = new();

    public TcpNetClient(ulong id, TcpClient tcp, ProtocolRegistry registry) : base(id, registry.Default) {
        this.tcp = tcp;
        this.registry = registry;
        stream = new MinecraftStream(tcp.GetStream());
    }

    public override void Send(IMessage message) {
        // Drop messages this protocol can't speak instead of crashing (cross-protocol safety net).
        if (!Protocol.CanEncode(message)) {
            Log.LogDebug("→ #{Client} {Packet} — no {Version} codec, dropped", Id, message.GetType().Name, Protocol.VersionName);
            return;
        }
        byte[] framed = Protocol.Frame(message);
        if (!WriteFramed(framed)) return;
        Log.LogDebug("→ #{Client} [{State}] {Packet} ({Bytes} B)",
            Id, State, message.GetType().Name, framed.Length);
    }

    public override void Send(CachedPacket packet) {
        if (!Protocol.CanEncode(packet.Message)) {
            Log.LogDebug("→ #{Client} {Packet} — no {Version} codec, dropped", Id, packet.Message.GetType().Name, Protocol.VersionName);
            return;
        }
        byte[] framed = packet.Framed(Protocol);
        if (!WriteFramed(framed)) return;
        Log.LogDebug("→ #{Client} [{State}] {Packet} ({Bytes} B, cached)",
            Id, State, packet.Message.GetType().Name, framed.Length);
    }

    /// <summary>
    /// Writes framed bytes to the socket under the write lock (read loop and tick-thread broadcasts both write).
    /// Returns false rather than throwing if the connection is already gone (a send can race a disconnect).
    /// </summary>
    bool WriteFramed(byte[] framed) {
        try {
            lock (writeLock) {
                stream.Write(framed, 0, framed.Length);
                stream.Flush();
            }
            return true;
        } catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) {
            Log.LogDebug("→ #{Client} send skipped — connection closed ({Reason})", Id, ex.GetType().Name);
            return false;
        }
    }

    public override void Disconnect() {
        try { tcp.Close(); } catch { /* already gone */ }
    }

    public override void EnableEncryption(byte[] sharedSecret) {
        lock (writeLock)
            stream.EnableEncryption(sharedSecret);
    }

    /// <summary>
    /// Blocking receive loop. Decodes packets against the connection's <see cref="NetClient.State"/>
    /// and hands each to <paramref name="onMessage"/>. Returns when the peer disconnects or faults.
    /// </summary>
    public void Receive(Action<NetClient, IMessage> onMessage) {
        try {
            // Sniff (peek) the first byte to pick framing before decoding: legacy clients don't open with a VarInt frame.
            Protocol = registry.Detect(stream.PeekUByte());
            if (ReferenceEquals(Protocol, registry.Default))
                Log.LogDebug("← #{Client} framing: {Name}", Id, Protocol.VersionName);
            else
                Log.LogInformation("← #{Client} legacy framing detected: {Name}", Id, Protocol.VersionName);

            while (tcp.Connected) {
                var message = Protocol.ReadMessage(stream, State, PacketDirection.Serverbound);
                if (message is null)
                    continue; // unknown/unsupported packet for this state

                Log.LogDebug("← #{Client} [{State}] {Packet}", Id, State, message.GetType().Name);
                onMessage(this, message);
            }
        } catch (FormatException ex) {
            // Malformed/undecodable packet (the message names the offending id/field).
            Log.LogWarning("← #{Client} protocol error, dropping connection: {Message}", Id, ex.Message);
        } catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException or InvalidOperationException) {
            Log.LogDebug("← #{Client} disconnected ({Reason})", Id, ex.GetType().Name);
        }
    }
}
