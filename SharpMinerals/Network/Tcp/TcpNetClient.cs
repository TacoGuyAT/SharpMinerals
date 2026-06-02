using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network.Tcp;

/// <summary>
/// A client connected over TCP. A blocking read loop on its own thread decodes inbound packets; outbound
/// packets go onto a per-client queue drained by one writer task. So a send only enqueues — it never blocks the
/// caller (e.g. the tick) on a slow socket: a client that can't keep up backs up its own queue (and is dropped
/// past <see cref="MaxQueued"/>) instead of stalling the simulation or other clients. A single writer also
/// gives per-client ordering for free, with no lock.
/// </summary>
public sealed class TcpNetClient : NetClient {
    static readonly ILogger Log = Logging.For("Net.Tcp");

    // A client whose queue grows past this (not draining its socket) is disconnected rather than buffered forever.
    const int MaxQueued = 8192;
    // Bounds a write to a wedged client (full TCP window) so the writer task can't block indefinitely.
    const int SendTimeoutMs = 30_000;

    readonly TcpClient tcp;
    readonly MinecraftStream stream;
    readonly ProtocolRegistry registry;

    readonly Channel<OutboundItem> outbound =
        Channel.CreateUnbounded<OutboundItem>(new UnboundedChannelOptions { SingleReader = true });
    int queued;
    readonly Task writer;

    // A queued send: framed bytes to write, or (Secret set) a request to switch the stream to encryption —
    // queued so the writer flips it in order, after the last plaintext packet.
    readonly record struct OutboundItem(byte[]? Frame, byte[]? Secret);

    public TcpNetClient(ulong id, TcpClient tcp, ProtocolRegistry registry) : base(id, registry.Default) {
        this.tcp = tcp;
        this.registry = registry;
        tcp.SendTimeout = SendTimeoutMs;
        stream = new MinecraftStream(tcp.GetStream());
        writer = Task.Run(WriteLoop);
    }

    public override void Send(IMessage message) {
        // Drop messages this protocol can't speak instead of crashing (cross-protocol safety net).
        if (!Protocol.CanEncode(message)) {
            Log.LogDebug("→ #{Client} {Packet} — no {Version} codec, dropped", Id, message.GetType().Name, Protocol.VersionName);
            return;
        }
        Enqueue(new OutboundItem(Protocol.Frame(message), null), message);
    }

    public override void Send(CachedPacket packet) {
        if (!Protocol.CanEncode(packet.Message)) {
            Log.LogDebug("→ #{Client} {Packet} — no {Version} codec, dropped", Id, packet.Message.GetType().Name, Protocol.VersionName);
            return;
        }
        Enqueue(new OutboundItem(packet.Framed(Protocol), null), packet.Message);
    }

    void Enqueue(in OutboundItem item, IMessage? message) {
        if (Interlocked.Increment(ref queued) > MaxQueued) {
            Log.LogWarning("→ #{Client} outbound backlog over {Max} — dropping slow client", Id, MaxQueued);
            Disconnect();
            return;
        }
        if (!outbound.Writer.TryWrite(item))
            Interlocked.Decrement(ref queued); // queue already completed (disconnecting) — drop silently
        else if (message is not null)
            Log.LogDebug("→ #{Client} [{State}] {Packet} queued", Id, State, message.GetType().Name);
    }

    /// <summary>Drains the outbound queue on a dedicated task: writes framed packets (one flush per drained
    /// batch) and applies encryption switches in order. Closes the socket when the queue completes or a write
    /// fails — so packets queued before <see cref="Disconnect"/> (e.g. a kick reason) are delivered first.</summary>
    async Task WriteLoop() {
        try {
            var reader = outbound.Reader;
            while (await reader.WaitToReadAsync()) {
                while (reader.TryRead(out var item)) {
                    Interlocked.Decrement(ref queued);
                    if (item.Secret is { } secret)
                        stream.EnableEncryption(secret);
                    else if (item.Frame is { } frame)
                        stream.Write(frame, 0, frame.Length);
                }
                stream.Flush();
            }
        } catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) {
            Log.LogDebug("→ #{Client} writer stopped — connection closed ({Reason})", Id, ex.GetType().Name);
        } finally {
            try { tcp.Close(); } catch { /* already gone */ }
        }
    }

    public override void Disconnect() => outbound.Writer.TryComplete(); // writer drains, flushes, then closes

    public override void EnableEncryption(byte[] sharedSecret) =>
        Enqueue(new OutboundItem(null, sharedSecret), null); // applied in order by the writer

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
        } finally {
            outbound.Writer.TryComplete(); // connection ended → let the writer task drain + close and exit
        }
    }
}
