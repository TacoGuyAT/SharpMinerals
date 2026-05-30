using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Components;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Protocols.JE763;
#if TEST_HARNESS
using SharpMinerals.Network.Buffers;
#endif

namespace SharpMinerals.Network.Handlers;

/// <summary>
/// Handles Play-state serverbound packets: digging, placement, movement and keep
/// alive. This is where wire messages drive the ECS world — breaking a block edits
/// the chunk and spawns a drop entity; placing reads the player's held item. Block
/// changes are broadcast to all in-game clients.
/// </summary>
public static class PlayPacketHandler {
    static readonly ILogger Log = Logging.For("Play");

    // Digging status codes (PlayerActionC2S.Status).
    const int DiggingStarted = 0;   // creative / instant break
    const int DiggingFinished = 2;  // survival break completed

    public static void Handle(NetClient client, IMessage message) {
        var server = Server.Instance;
        if (server is null) return;

        switch (message) {
            case PlayerActionC2S action:
                HandleDigging(server, client, action);
                break;

            case UseItemOnC2S use:
                HandlePlacement(server, client, use);
                break;

            case SetPlayerPositionC2S pos:
                MovePlayer(server, client, pos.X, pos.Y, pos.Z, null, null);
                break;

            case SetPlayerPositionAndRotationC2S pr:
                MovePlayer(server, client, pr.X, pr.Y, pr.Z, pr.Yaw, pr.Pitch);
                break;

            case SetPlayerRotationC2S rot:
                MovePlayer(server, client, null, null, null, rot.Yaw, rot.Pitch);
                break;

            case InteractEntityC2S interact:
                HandleInteract(server, client, interact);
                break;

            case ChatMessageC2S chat:
                SubmitChat(server, client, chat.Message);
                break;

            case ChatCommandC2S command:
                SubmitChat(server, client, "/" + command.Command);
                break;

            case ClickContainerC2S click:
                server.Containers.OnClick(server, client.Id, click);
                break;

            case CloseContainerC2S close:
                server.Containers.OnClose(server, client.Id, close.WindowId);
                break;

            case SetHeldItemC2S held:
                HandleSetHeldItem(server, client, held);
                break;

            case SetCreativeModeSlotC2S creative:
                HandleCreativeSlot(server, client, creative);
                break;

            case KeepAliveC2S:
                // Liveness confirmed; nothing else to do for the base server.
                break;

#if TEST_HARNESS
            case CustomPayloadC2S payload:
                HandleCustomPayload(payload);
                break;
#endif
        }
    }

    static void HandleDigging(Server server, NetClient client, PlayerActionC2S action) {
        if (action.Status is not (DiggingStarted or DiggingFinished)) {
            // Cancelled or other stages — just acknowledge.
            client.Send(new AckBlockChangeS2C(action.Sequence));
            return;
        }

        var world = server.DefaultWorld;
        var broken = world.BreakBlock(action.Position);

        client.Send(new AckBlockChangeS2C(action.Sequence));
        if (broken.IsAir)
            return; // nothing was there

        // If a container block was broken, close it for anyone who had it open.
        if (broken.Has<Container>())
            server.Containers.ForceCloseChest(server, action.Position);

        BroadcastPlay(server, new BlockUpdateS2C(action.Position, TypeMapper.StateId(BlockRegistry.Air)));
        // The dropped ECS entity spawned by BreakBlock is announced and made pickable by DropSystem.
    }

    static void HandlePlacement(Server server, NetClient client, UseItemOnC2S use) {
        client.Send(new AckBlockChangeS2C(use.Sequence));

        if (!server.TryGetPlayer(client.Id, out var handle))
            return;

        // Right-clicking a container block (chest) opens it instead of placing.
        var clicked = handle.World.GetBlock(use.Position);
        if (clicked.Has<Container>()) {
            var entity = handle.World.GetBlockEntity(use.Position) ?? CreateBlockEntity(handle.World, use.Position, clicked);
            server.Containers.Open(server, client.Id, entity);
            return;
        }

        // Resolve the block to place from the player's held item (selected hotbar slot).
        var inventory = handle.World.Ecs.Get<EntityInventory>(handle.Entity);
        var block = inventory.Held.Type?.PlacedBlock;
        if (block is null)
            return;

        var target = Offset(use.Position, (BlockFace)use.Face);
        if (handle.World.PlaceBlock(target, block))
            BroadcastPlay(server, new BlockUpdateS2C(target, TypeMapper.StateId(block)));
    }

    static void MovePlayer(Server server, NetClient client, double? x, double? y, double? z, float? yaw, float? pitch) {
        if (!server.TryGetPlayer(client.Id, out var handle) || !handle.World.Ecs.IsAlive(handle.Entity))
            return;

        ref var transform = ref handle.World.Ecs.Get<Transform>(handle.Entity);
        if (x is not null) transform.X = x.Value;
        if (y is not null) transform.Y = y.Value;
        if (z is not null) transform.Z = z.Value;
        if (yaw is not null) transform.Yaw = yaw.Value;
        if (pitch is not null) transform.Pitch = pitch.Value;

        // Mirror the movement to everyone else so they see this player move.
        PlayerVisibility.OnMove(server, client);
    }

    static void SubmitChat(Server server, NetClient client, string input) {
        if (server.Commands is null || !server.TryGetPlayer(client.Id, out var handle) || !handle.World.Ecs.IsAlive(handle.Entity))
            return;
        // The player's ChatSender component routes the text: '/cmd' runs a command as
        // the player, plain text broadcasts as chat.
        var sender = handle.World.Ecs.Get<ChatSender>(handle.Entity);
        _ = server.Commands.HandleAsync(sender, input);
    }

    static void HandleInteract(Server server, NetClient client, InteractEntityC2S interact) {
        const int Attack = 1;
        string verb = interact.Type == Attack ? "attacked" : "interacted with";

        // Resolve who got hit, for a readable log (this is the player-visibility test signal).
        string target = $"entity {interact.TargetId}";
        foreach (var (_, handle) in server.Players) {
            if (!handle.World.Ecs.IsAlive(handle.Entity)) continue;
            var np = handle.World.Ecs.Get<NetworkedPlayer>(handle.Entity);
            if (np.EntityId == interact.TargetId) { target = np.Name; break; }
        }

        Log.LogInformation("#{Client} {Verb} {Target}", client.Id, verb, target);
    }

#if TEST_HARNESS
    static void HandleCustomPayload(CustomPayloadC2S payload) {
        // The harness reports command results on its control channel; log them so a
        // file-driven test scenario's outcome shows up server-side.
        if (payload.Channel != "sharptester:cmd")
            return;

        try {
            using var ms = new MemoryStream(payload.Data, writable: false);
            var reader = new MinecraftStream(ms);
            Log.LogInformation("[test client] {Result}", reader.ReadString());
        } catch {
            Log.LogInformation("[test client] {Bytes} bytes on {Channel}", payload.Data.Length, payload.Channel);
        }
    }
#endif

    static void HandleSetHeldItem(Server server, NetClient client, SetHeldItemC2S held) {
        if (!server.TryGetPlayer(client.Id, out var handle) || !handle.World.Ecs.IsAlive(handle.Entity))
            return;
        var inventory = handle.World.Ecs.Get<EntityInventory>(handle.Entity);
        inventory.SelectedSlot = System.Math.Clamp(held.Slot, 0, EntityInventory.HotbarSize - 1);
    }

    static void HandleCreativeSlot(Server server, NetClient client, SetCreativeModeSlotC2S creative) {
        if (!server.TryGetPlayer(client.Id, out var handle) || !handle.World.Ecs.IsAlive(handle.Entity))
            return;
        if (!ContainerManager.TryPlayerWindowToStorage(creative.Slot, out int index))
            return; // crafting / drop slot — ignored
        var inventory = handle.World.Ecs.Get<EntityInventory>(handle.Entity);
        inventory.Storage[index] = creative.VanillaItemId is { } id && TypeMapper.FromItemId(id) is { } type
            ? new ItemStack(type, creative.Count)
            : default;
    }

    /// <summary>Creates and registers a block entity at a position (e.g. a chest's storage).</summary>
    static BlockEntity CreateBlockEntity(World world, Vector3i pos, BlockType type) {
        var entity = new BlockEntity(pos, type);
        world.SetBlockEntity(entity);
        return entity;
    }

    /// <summary>Broadcasts a packet to every client that has reached the Play state.</summary>
    static void BroadcastPlay(Server server, IMessage message) =>
        server.NetServer.Broadcast(message, c => c.State == ConnectionState.Play);

    /// <summary>The block adjacent to <paramref name="pos"/> across the given face.</summary>
    static Vector3i Offset(Vector3i pos, BlockFace face) => face switch {
        BlockFace.Bottom => pos + new Vector3i(0, -1, 0),
        BlockFace.Top => pos + new Vector3i(0, 1, 0),
        BlockFace.North => pos + new Vector3i(0, 0, -1),
        BlockFace.South => pos + new Vector3i(0, 0, 1),
        BlockFace.West => pos + new Vector3i(-1, 0, 0),
        BlockFace.East => pos + new Vector3i(1, 0, 0),
        _ => pos,
    };
}
