using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Level;
using SharpMinerals.Level.Systems;
using SharpMinerals.Math;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Chat;
using SharpMinerals.Commands;
using SharpMinerals.Entities;



#if TEST_HARNESS
using SharpMinerals.Network.Buffers;
#endif

namespace SharpMinerals.Network.Handlers;

/// <summary>
/// Handles Play-state serverbound packets (digging, placement, movement, keep-alive, chat, containers):
/// the seam where wire messages drive the ECS world. Block changes are broadcast to all in-game clients.
/// </summary>
public sealed class PlayPacketHandler {
    static readonly ILogger Log = Logging.For("Play");

    // Player Action status codes (PlayerActionC2S.Status).
    const int DiggingStarted = 0;   // creative / instant break
    const int DiggingFinished = 2;  // survival break completed
    const int DropItemStack = 3;    // drop the whole held stack (Ctrl+Q)
    const int DropItem = 4;         // drop one of the held item (Q)
    const int CreativeDropSlot = -1; // SetCreativeModeSlot slot meaning "throw the carried item out"

    readonly Server server;

    public PlayPacketHandler(Server server) => this.server = server;

    public void Handle(NetClient client, IMessage message) {
        switch (message) {
            // World-mutating packets are deferred to the tick's single-writer drain phase so chunk edits
            // never race the simulation or autosave (<=1 tick of latency).
            case PlayerActionC2S action:
                server.Events.Defer(() => HandleDigging(client, action));
                break;

            case UseItemOnC2S use:
                server.Events.Defer(() => HandlePlacement(client, use));
                break;

            case SetPlayerPositionC2S pos:
                MovePlayer(client, pos.X, pos.Y, pos.Z, null, null);
                break;

            case SetPlayerPositionAndRotationC2S pr:
                MovePlayer(client, pr.X, pr.Y, pr.Z, pr.Yaw, pr.Pitch);
                break;

            case SetPlayerRotationC2S rot:
                MovePlayer(client, null, null, null, rot.Yaw, rot.Pitch);
                break;

            case InteractEntityC2S interact:
                HandleInteract(client, interact);
                break;

            case SwingArmC2S swing:
                HandleSwing(client, swing);
                break;

            case EntityActionC2S action:
                HandleEntityAction(client, action);
                break;

            case ChatMessageC2S chat:
                if(!server.TryGetPlayer(client.Id, out var chatContext) || !chatContext.World.Ecs.IsAlive(chatContext.Entity))
                    return;
                var chatSender = chatContext.World.Ecs.Get<SenderEntityComponent>(chatContext.Entity);
                if(chat.Message.StartsWith('/')) {
                    _ = server.CommandDispatcher.ExecuteAsync(chatSender, chat.Message[1..], client);
                } else {
                    chatSender.SendMessage(server, chat.Message);
                }
                break;

            case ChatCommandC2S command:
                if(!server.TryGetPlayer(client.Id, out var commandContext) || !commandContext.World.Ecs.IsAlive(commandContext.Entity))
                    return;
                // The ChatSender is the reply sink; the connection gives the command source its player entity.
                var commandSender = commandContext.World.Ecs.Get<SenderEntityComponent>(commandContext.Entity);
                _ = server.CommandDispatcher.ExecuteAsync(commandSender, command.Command, client);
                break;

            case ClickContainerC2S click:
                server.Events.Defer(() => server.Containers.OnClick(server, client.Id, click));
                break;

            case CloseContainerC2S close:
                server.Events.Defer(() => server.Containers.OnClose(server, client.Id, close.WindowId));
                break;

            case SetHeldItemC2S held:
                server.Events.Defer(() => HandleSetHeldItem(client, held));
                break;

            case SetCreativeModeSlotC2S creative:
                server.Events.Defer(() => HandleCreativeSlot(client, creative));
                break;

            case KeepAliveC2S:
                // Liveness confirmed; nothing else to do for the base server.
                break;

            case ConfirmTeleportationC2S confirm:
                server.ConfirmTeleport(client.Id, confirm.TeleportId);
                break;

            case CommandSuggestionsRequestC2S suggest:
                HandleSuggestions(client, suggest);
                break;

            // Legacy (JE61) movement/digging decode into the generic messages above and share their cases.
            // Only placement differs: a creative legacy client carries the held item in the packet.
            case LegacyBlockPlacementC2S lplace:
                server.Events.Defer(() => HandleLegacyPlacement(client, lplace));
                break;

#if TEST_HARNESS
            case CustomPayloadC2S payload:
                HandleCustomPayload(client, payload);
                break;
#endif
        }
    }

    // Answer a tab-complete request: ask Brigadier for completions (source built for this player so
    // .Requires and ask_server providers see the right state) and send back the matches.
    void HandleSuggestions(NetClient client, CommandSuggestionsRequestC2S req) {
        if (!server.TryGetPlayer(client.Id, out var pc) || !pc.World.Ecs.IsAlive(pc.Entity)) {
            Log.LogDebug("suggest #{Tx}: no live player for client #{Client}", req.TransactionId, client.Id);
            return;
        }
        try {
            var sender = pc.World.Ecs.Get<SenderEntityComponent>(pc.Entity);
            // Dispatcher strips the leading '/' and keeps the range in client coordinates (providers complete inline).
            var (start, length, matches) = server.CommandDispatcher.Suggest(sender, req.Text, client);
            Log.LogDebug("suggest #{Tx} '{Text}' -> {Count} match(es) [{Start}+{Length}]: {Matches}",
                req.TransactionId, req.Text, matches.Count, start, length, string.Join(", ", matches));
            // Always reply (even with zero matches) so the client gets a definitive answer.
            client.Send(new CommandSuggestionsResponseS2C(req.TransactionId, start, length, matches));
        } catch (Exception ex) {
            Log.LogWarning(ex, "suggest #{Tx} '{Text}' failed", req.TransactionId, req.Text);
        }
    }

    void HandleDigging(NetClient client, PlayerActionC2S action) {
        if (action.Status is DropItemStack or DropItem) {
            HandleDropHeld(client, wholeStack: action.Status == DropItemStack);
            return;
        }
        if (action.Status is not (DiggingStarted or DiggingFinished)) {
            client.Send(new AckBlockChangeS2C(action.Sequence)); // cancelled/other stage: just ack
            return;
        }

        var world = server.DefaultWorld;
        server.TryGetPlayer(client.Id, out var breaker);
        var broken = world.BreakBlock(action.Position, breaker);

        client.Send(new AckBlockChangeS2C(action.Sequence));
        if (broken.IsAir)
            return; // nothing was there

        BroadcastBlockChange(world, new BlockUpdateS2C(action.Position, BlockRegistry.Air));
        // The block above may have lost its support (sand/gravel); let it fall.
        FallingBlockSystem.TryStartFalling(server, world, action.Position + new Vector3i(0, 1, 0));
    }

    /// <summary>Drop key (Q): toss the held item (whole stack on Ctrl+Q, else one), resync the window, update the visible hand.</summary>
    void HandleDropHeld(NetClient client, bool wholeStack) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        ref var held = ref inventory.Held;
        if (held.IsEmpty) return;

        var tossed = held;
        if (wholeStack || held.Count <= 1) {
            held = default;
        } else {
            tossed.Count = 1;
            held.Count -= 1;
        }

        context.World.TossItem(context.World.Ecs.Get<TransformEntityComponent>(context.Entity), tossed);
        context.SyncInventory();
    }

    /// <summary>Legacy (1.5.2) block placement: a creative client sends the held item in the packet, placed directly.</summary>
    void HandleLegacyPlacement(NetClient client, LegacyBlockPlacementC2S place) {
        if (place.Direction > 5 || place.ItemId == -1)
            return; // "use item" / empty hand
        if (client.Protocol.Types.FromItemId(place.ItemId) is not BlockType placed || placed.PlacedBlock is not { } block)
            return; // not a placeable block we know
        var target = Offset(new Vector3i(place.X, place.Y, place.Z), (BlockFace)place.Direction);
        if (!server.DefaultWorld.PlaceBlock(target, block))
            return;
        BroadcastBlockChange(server.DefaultWorld, new BlockUpdateS2C(target, block));
        FallingBlockSystem.TryStartFalling(server, server.DefaultWorld, target); // sand/gravel placed over air falls
    }

    void HandlePlacement(NetClient client, UseItemOnC2S use) {
        client.Send(new AckBlockChangeS2C(use.Sequence));

        if (!server.TryGetPlayer(client.Id, out var context))
            return;

        // A block with interaction behavior (e.g. a container) consumes the right-click instead of placing.
        var clicked = context.World.GetBlock(use.Position);
        var interaction = new BlockContext { World = context.World, Position = use.Position, Block = clicked, Actor = context };
        bool interacted = false;
        foreach (var b in clicked.GetAll<IInteract>()) { b.OnInteract(in interaction); interacted = true; }
        if (interacted)
            return;

        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        var held = inventory.Held;
        var block = held.Type?.PlacedBlock;
        if (block is null)
            return;

        var target = Offset(use.Position, (BlockFace)use.Face);
        if (!context.World.PlaceBlock(target, block))
            return;

        // Prefer the variant carried by the held item (e.g. wool colour); else orient a facing block toward the player.
        BlockState? state = held.State?.Clone();
        if (state is null && block.TryGet<StatesBlockDescriptor>(out var props) && props.IndexOf(State.Facing) >= 0) {
            var yaw = context.World.Ecs.Get<TransformEntityComponent>(context.Entity).Yaw;
            state = new BlockState(block).Set(State.Facing, FacingTowardPlayer(yaw));
        }
        if (state is not null)
            context.World.SetBlockState(target, state);
        BroadcastBlockChange(context.World, new BlockUpdateS2C(target, block, state)); // null state => block default

        FallingBlockSystem.TryStartFalling(server, context.World, target); // sand/gravel placed over air falls
    }

    void MovePlayer(NetClient client, double? x, double? y, double? z, float? yaw, float? pitch) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;

        // Ignore positions until a pending teleport is confirmed: they're stale and would bounce the player back.
        if (server.IsTeleportPending(client.Id))
            return;

        ref var transform = ref context.World.Ecs.Get<TransformEntityComponent>(context.Entity);
        if (x is not null) transform.X = x.Value;
        if (y is not null) transform.Y = y.Value;
        if (z is not null) transform.Z = z.Value;
        if (yaw is not null) transform.Yaw = yaw.Value;
        if (pitch is not null) transform.Pitch = pitch.Value;

        // Re-file the player in the spatial index; the per-tick movement + chunk systems project the move to
        // other players and stream new columns as they cross chunk boundaries.
        server.Events.Publish(new EntityMoved(context.World, context.Entity));
    }

    void HandleInteract(NetClient client, InteractEntityC2S interact) {
        const int Attack = 1;
        string verb = interact.Type == Attack ? "attacked" : "interacted with";

        // Resolve who got hit, for a readable log.
        string target = $"entity {interact.TargetId}";
        foreach (var (_, context) in server.Players) {
            if (!context.World.Ecs.IsAlive(context.Entity)) continue;
            var np = context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity);
            if (np.EntityId == interact.TargetId) { target = np.Name; break; }
        }

        Log.LogInformation("#{Client} {Verb} {Target}", client.Id, verb, target);
    }

    /// <summary>Player swung their arm: animate it on every other in-world client.</summary>
    void HandleSwing(NetClient client, SwingArmC2S swing) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        int eid = context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity).EntityId;
        var animation = swing.Hand == 0 ? EntityAnimation.SwingMainArm : EntityAnimation.SwingOffArm;
        server.NetServer.Broadcast(new EntityAnimationS2C(eid, animation), c => c.InWorld && c.Id != client.Id);
    }

    /// <summary>Player toggled a shared-flags state (sneak/sprint): update the persisted flags (so joiners
    /// learn the state) and broadcast them as entity metadata to every other in-world client.</summary>
    void HandleEntityAction(NetClient client, EntityActionC2S action) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        ref var np = ref context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity);
        switch (action.Action) {
            case EntityActionKind.StartSneaking:  np.Flags |= EntityFlags.Sneaking;  break;
            case EntityActionKind.StopSneaking:   np.Flags &= ~EntityFlags.Sneaking;  break;
            case EntityActionKind.StartSprinting: np.Flags |= EntityFlags.Sprinting; break;
            case EntityActionKind.StopSprinting:  np.Flags &= ~EntityFlags.Sprinting; break;
            default: return; // leave-bed / horse / etc. not modelled
        }
        server.NetServer.Broadcast(new EntityFlagsS2C(np.EntityId, np.Flags), c => c.InWorld && c.Id != client.Id);
    }

#if TEST_HARNESS
    void HandleCustomPayload(NetClient client, CustomPayloadC2S payload) {
        // The harness reports each command's result on its control channel. Log it (so a crash leaves a trail)
        // and hand it to any subscribed in-process harness (Server.TestClientReply) for assertions.
        if (payload.Channel != "sharptester:cmd")
            return;

        string result;
        try {
            using var ms = new MemoryStream(payload.Data, writable: false);
            result = new MinecraftStream(ms).ReadString();
        } catch {
            Log.LogInformation("[test client] {Bytes} bytes on {Channel}", payload.Data.Length, payload.Channel);
            return;
        }
        Log.LogInformation("[test client] {Result}", result);
        server.RaiseTestClientReply(client.Id, result);
    }
#endif

    void HandleSetHeldItem(NetClient client, SetHeldItemC2S held) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        inventory.SelectedSlot = System.Math.Clamp(held.Slot, 0, InventoryEntityComponent.HotbarSize - 1);
        // The held item others see may have changed; the per-tick equipment diff picks it up.
    }

    void HandleCreativeSlot(NetClient client, SetCreativeModeSlotC2S creative) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        // A null stack = the client placed an item this server has no type for; the client's predicted view
        // is wrong, so correct it (honouring the cursor where the player legitimately grabbed an item).
        // Warned as an OVERLAY so a re-sending creative client doesn't spam chat.
        if (creative.Stack is not { } stack) {
            var current = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
            if (creative.Slot == CreativeDropSlot) {
                // Thrown from the cursor: the bad item never existed here; just clear the cursor.
                client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(current), default));
            } else if (ContainerManager.TryPlayerWindowToStorage(creative.Slot, out int swapIndex) && !current.Storage[swapIndex].IsEmpty) {
                // Swap onto a filled slot: the player grabbed its item onto the cursor; empty the slot, resync, drop the bad item.
                var grabbed = current.Storage[swapIndex];
                current.Storage[swapIndex] = default;
                client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(current), grabbed));
            } else {
                // Empty/unsupported slot: revert just that slot, leaving the cursor as the client has it.
                client.Send(new SetContainerSlotS2C(0, 0, creative.Slot, default));
            }
            client.Send(new SystemChatMessageS2C(
                new TextComponent("That item isn't available on this server.").SetColor(TextColor.Red), Overlay: true));
            return;
        }
        // Slot -1 is the creative "throw": toss the carried item into the world rather than storing it.
        if (creative.Slot == CreativeDropSlot) {
            if (!stack.IsEmpty)
                context.World.TossItem(context.World.Ecs.Get<TransformEntityComponent>(context.Entity), stack);
            return;
        }
        if (!ContainerManager.TryPlayerWindowToStorage(creative.Slot, out int index))
            return; // crafting slot - ignored
        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        inventory.Storage[index] = stack; // an empty (non-null) stack here is a deliberate clear
    }

    /// <summary>Broadcasts a block change to in-world clients within view of it (modern and legacy); each
    /// protocol encodes it in its own format. A client outside its view distance doesn't hold the chunk and
    /// receives the current block when the chunk streams in, so range-gating is a pure bandwidth saving.</summary>
    void BroadcastBlockChange(World world, BlockUpdateS2C message) =>
        server.BroadcastInRange(world, message.Position.X + 0.5, message.Position.Z + 0.5, message);

    static string FacingTowardPlayer(float yaw) {
        // Player yaw 0=south, 90=west, 180=north, 270=east; the block faces the player (opposite).
        int dir = (int)System.Math.Floor((yaw + 45f) / 90f) & 3;
        return dir switch { 0 => "north", 1 => "east", 2 => "south", _ => "west" };
    }

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
