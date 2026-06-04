using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Tcp;

/// <summary>
/// A client connected over TCP. A blocking read loop on its own thread decodes inbound packets; outbound
/// packets go onto a per-client two-lane queue drained by one writer task. So a send only enqueues - it never
/// blocks the caller (e.g. the tick) on a slow socket: a client that can't keep up backs up its own queue (and
/// is dropped past <see cref="MaxQueued"/>) instead of stalling the simulation or other clients.
/// <para>Two priority lanes: <b>instant</b> (gameplay - movement, block updates, chat, health, ...) is fully
/// drained before <b>bulk</b> (chunk data), and a newly-arrived instant packet preempts the bulk backlog. So a
/// chunk-streaming burst never delays instant feedback; chunks just trail behind. A single writer also gives
/// per-lane ordering for free, with no lock.</para>
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

    // Two lanes drained by one writer: instant first, bulk (chunks) deferred behind it.
    readonly Channel<OutboundItem> priorityQueue = NewLane();
    readonly Channel<OutboundItem> queue = NewLane();
    int queued;
    readonly Task writer;

    static Channel<OutboundItem> NewLane() =>
        Channel.CreateUnbounded<OutboundItem>(new UnboundedChannelOptions { SingleReader = true });

    // A queued send: framed bytes to write, or (Secret set) a request to switch the stream to encryption,
    // queued so the writer flips it in order, after the last plaintext packet.
    readonly record struct OutboundItem(byte[]? Frame, byte[]? Secret);

    enum Lane { Instant, Bulk }

    // Bulk lane = chunk data (deferred behind gameplay) + the player's position sync, which must stay ordered
    // AFTER the chunks it's placed onto (else the client is positioned before terrain loads and sinks). Both
    // share the lane so instant gameplay can overtake them but they never overtake each other.
    static Lane LaneOf(IMessage message) => message switch {
        ChunkDataS2C or SynchronizePlayerPositionS2C => Lane.Bulk,
        _ => Lane.Instant,
    };

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
            Log.LogDebug("-> #{Client} {Packet} - no {Version} codec, dropped", Id, message.GetType().Name, Protocol.VersionName);
            return;
        }
        Enqueue(new OutboundItem(Protocol.Frame(message), null), LaneOf(message), message);
    }

    public override void Send(CachedPacket packet) {
        if (!Protocol.CanEncode(packet.Message)) {
            Log.LogDebug("-> #{Client} {Packet} - no {Version} codec, dropped", Id, packet.Message.GetType().Name, Protocol.VersionName);
            return;
        }
        Enqueue(new OutboundItem(packet.Framed(Protocol), null), LaneOf(packet.Message), packet.Message);
    }

    void Enqueue(in OutboundItem item, Lane lane, IMessage? message) {
        if (Interlocked.Increment(ref queued) > MaxQueued) {
            Log.LogWarning("-> #{Client} outbound backlog over {Max} - dropping slow client", Id, MaxQueued);
            Disconnect();
            return;
        }
        var channel = lane == Lane.Bulk ? queue : priorityQueue;
        if (!channel.Writer.TryWrite(item))
            Interlocked.Decrement(ref queued); // queue already completed (disconnecting) - drop silently
        else if (message is not null)
            Log.LogDebug("-> #{Client} [{State}] {Packet} queued ({Lane})", Id, State, message.GetType().Name, lane);
    }

    /// <summary>Drains the queue on a dedicated task: instant lane first, then one bulk item at a time, yielding
    /// to any instant packet that arrives between bulk items. Flushes once per drained batch. Closes the socket
    /// when both lanes complete or a write fails - so packets queued before <see cref="Disconnect"/> are sent.</summary>
    async Task WriteLoop() {
        try {
            while (true) {
                while (priorityQueue.Reader.TryRead(out var hi)) { Interlocked.Decrement(ref queued); WriteItem(hi); }
                if (queue.Reader.TryRead(out var lo)) { Interlocked.Decrement(ref queued); WriteItem(lo); continue; }
                stream.Flush();                 // both lanes momentarily empty - flush the batch, then wait
                if (!await WaitForData()) break; // both lanes completed -> done
            }
        } catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) {
            Log.LogDebug("-> #{Client} writer stopped - connection closed ({Reason})", Id, ex.GetType().Name);
        } finally {
            try { tcp.Close(); } catch { /* already gone */ }
        }
    }

    void WriteItem(in OutboundItem item) {
        if (item.Secret is { } secret) stream.EnableEncryption(secret);
        else if (item.Frame is { } frame) stream.Write(frame, 0, frame.Length);
    }

    // Waits until either lane has an item; returns false only once BOTH lanes are completed and empty.
    async Task<bool> WaitForData() {
        var hi = priorityQueue.Reader.WaitToReadAsync().AsTask();
        var lo = queue.Reader.WaitToReadAsync().AsTask();
        await Task.WhenAny(hi, lo);
        if (hi.IsCompletedSuccessfully && hi.Result) return true;
        if (lo.IsCompletedSuccessfully && lo.Result) return true;
        // Whichever completed did so as false (lane closed); await the other before declaring both done.
        if (!hi.IsCompleted) return await hi;
        if (!lo.IsCompleted) return await lo;
        return false;
    }

    public override void Disconnect() {
        priorityQueue.Writer.TryComplete(); // writer drains both lanes, flushes, then closes
        queue.Writer.TryComplete();
    }

    public override void EnableEncryption(byte[] sharedSecret) =>
        // Instant lane: applied in order, after the prior login packets (legacy 1.5.2 only, pre-Play -> no bulk).
        Enqueue(new OutboundItem(null, sharedSecret), Lane.Instant, null);

    /// <summary>
    /// Blocking receive loop. Decodes packets against the connection's <see cref="NetClient.State"/>
    /// and hands each to <paramref name="onMessage"/>. Returns when the peer disconnects or faults.
    /// </summary>
    public void Receive(Action<NetClient, IMessage> onMessage) {
        try {
            // Sniff (peek) the first byte to pick framing before decoding: legacy clients don't open with a VarInt frame.
            Protocol = registry.Detect(stream.PeekUByte());
            if (ReferenceEquals(Protocol, registry.Default))
                Log.LogDebug("<- #{Client} framing: {Name}", Id, Protocol.VersionName);
            else
                Log.LogInformation("<- #{Client} legacy framing detected: {Name}", Id, Protocol.VersionName);

            while (tcp.Connected) {
                var message = Protocol.ReadMessage(stream, State, PacketDirection.Serverbound);
                if (message is null)
                    continue; // unknown/unsupported packet for this state

                Log.LogDebug("<- #{Client} [{State}] {Packet}", Id, State, message.GetType().Name);
                onMessage(this, message);
            }
        } catch (FormatException ex) {
            // Malformed/undecodable packet (the message names the offending id/field).
            Log.LogWarning("<- #{Client} protocol error, dropping connection: {Message}", Id, ex.Message);
        } catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException or InvalidOperationException) {
            Log.LogDebug("<- #{Client} disconnected ({Reason})", Id, ex.GetType().Name);
        } finally {
            Disconnect(); // connection ended -> complete both lanes so the writer task drains + closes and exits
        }
    }
}
