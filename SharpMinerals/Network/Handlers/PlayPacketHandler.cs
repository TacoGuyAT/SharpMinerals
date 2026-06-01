using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Level;
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
/// Handles Play-state serverbound packets: digging, placement, movement and keep
/// alive. This is where wire messages drive the ECS world — breaking a block edits
/// the chunk and spawns a drop entity; placing reads the player's held item. Block
/// changes are broadcast to all in-game clients.
/// <para/>
/// An instance bound to its <see cref="Server"/> (injected once), so the handlers read it from a
/// field rather than receiving it on every call.
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
            // World-mutating packets are deferred to the tick loop's single-writer drain phase, so
            // chunk edits never race the simulation or autosave (adds ≤1 tick of latency).
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

            // Legacy (JE61) movement/digging decode into the GENERIC messages above (SetPlayerPosition*,
            // PlayerAction) — handled by the same cases, no legacy path. Only placement differs (a
            // creative legacy client carries the held item in the packet, not the server inventory).
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

    // Answer a tab-complete request: ask Brigadier for completions of the partial text (the source is built
    // for this player so .Requires and ask_server providers see the right state) and send back the matches.
    void HandleSuggestions(NetClient client, CommandSuggestionsRequestC2S req) {
        if (!server.TryGetPlayer(client.Id, out var pc) || !pc.World.Ecs.IsAlive(pc.Entity)) {
            Log.LogDebug("suggest #{Tx}: no live player for client #{Client}", req.TransactionId, client.Id);
            return;
        }
        try {
            var sender = pc.World.Ecs.Get<SenderEntityComponent>(pc.Entity);
            // The dispatcher strips the leading '/' the client sends and keeps the range in the client's
            // coordinates (our providers complete inline, so this is synchronous).
            var (start, length, matches) = server.CommandDispatcher.Suggest(sender, req.Text, client);
            Log.LogDebug("suggest #{Tx} '{Text}' -> {Count} match(es) [{Start}+{Length}]: {Matches}",
                req.TransactionId, req.Text, matches.Count, start, length, string.Join(", ", matches));
            // Always reply (even with zero matches) so the client gets a definitive answer for this transaction.
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
        if (broken.Has<ContainerBlockDescriptor>())
            server.Containers.ForceCloseChest(server, action.Position);

        BroadcastBlockChange(new BlockUpdateS2C(action.Position, BlockRegistry.Air));
        // The dropped ECS entity spawned by BreakBlock is announced by EntityNetworking and collected by ItemPickupSystem.

        // The block above the one just removed may have lost its support (sand/gravel) — let it fall.
        EntityNetworking.TryStartFalling(server, world, action.Position + new Vector3i(0, 1, 0));
    }

    /// <summary>The player pressed Q (drop): toss the held item — the whole stack (Ctrl+Q) or one (Q) — out
    /// in front of them as a world item, then resync the window and update the hand others see.</summary>
    void HandleDropHeld(NetClient client, bool wholeStack) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        ref var held = ref inventory.Held;
        if (held.IsEmpty) return;

        var tossed = held;
        if (wholeStack || held.Count <= 1) {
            held = default; // dropped the whole stack
        } else {
            tossed.Count = 1; // a single item leaves the hand
            held.Count -= 1;
        }

        context.World.TossItem(context.World.Ecs.Get<TransformEntityComponent>(context.Entity), tossed);
        client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inventory), default));
        server.Events.Publish(new PlayerInventoryChanged(context));
    }

    /// <summary>
    /// Legacy (1.5.2) block placement. A creative client sends the held item in the packet, so we place
    /// that block directly (no server-side inventory needed). Deferred to the tick (chunk mutation).
    /// </summary>
    void HandleLegacyPlacement(NetClient client, LegacyBlockPlacementC2S place) {
        if (place.Direction > 5 || place.ItemId == -1)
            return; // "use item" / empty hand
        if (client.Protocol.Types.FromItemId(place.ItemId) is not BlockType placed || placed.PlacedBlock is not { } block)
            return; // not a placeable block we know
        var target = Offset(new Vector3i(place.X, place.Y, place.Z), (BlockFace)place.Direction);
        if (!server.DefaultWorld.PlaceBlock(target, block))
            return;
        BroadcastBlockChange(new BlockUpdateS2C(target, block));
        EntityNetworking.TryStartFalling(server, server.DefaultWorld, target); // sand/gravel placed over air falls
    }

    void HandlePlacement(NetClient client, UseItemOnC2S use) {
        client.Send(new AckBlockChangeS2C(use.Sequence));

        if (!server.TryGetPlayer(client.Id, out var context))
            return;

        // Right-clicking a container block (chest) opens it instead of placing.
        var clicked = context.World.GetBlock(use.Position);
        if (clicked.Has<ContainerBlockDescriptor>()) {
            var entity = context.World.GetBlockEntity(use.Position) ?? CreateBlockEntity(context.World, use.Position, clicked);
            server.Containers.Open(server, client.Id, entity);
            return;
        }

        // Resolve the block to place from the player's held item (selected hotbar slot).
        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        var held = inventory.Held;
        var block = held.Type?.PlacedBlock;
        if (block is null)
            return;

        var target = Offset(use.Position, (BlockFace)use.Face);
        if (!context.World.PlaceBlock(target, block))
            return;

        // The placed block's state: prefer the variant carried by the held item (e.g. wool
        // colour); otherwise orient a facing block (chest, …) toward the player.
        BlockState? state = held.State?.Clone();
        if (state is null && block.TryGet<StatesBlockDescriptor>(out var props) && props.IndexOf(State.Facing) >= 0) {
            var yaw = context.World.Ecs.Get<TransformEntityComponent>(context.Entity).Yaw;
            state = new BlockState(block).Set(State.Facing, FacingTowardPlayer(yaw));
        }
        if (state is not null)
            context.World.SetBlockState(target, state);
        // Codec maps to this client's wire id; null state ⇒ the block's default.
        BroadcastBlockChange(new BlockUpdateS2C(target, block, state));
        EntityNetworking.TryStartFalling(server, context.World, target); // sand/gravel placed over air falls
    }

    void MovePlayer(NetClient client, double? x, double? y, double? z, float? yaw, float? pitch) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;

        // Until the client confirms a pending teleport, its position is stale (pre-teleport) —
        // accepting it would bounce a teleported/restored player back. Ignore it.
        if (server.IsTeleportPending(client.Id))
            return;

        ref var transform = ref context.World.Ecs.Get<TransformEntityComponent>(context.Entity);
        if (x is not null) transform.X = x.Value;
        if (y is not null) transform.Y = y.Value;
        if (z is not null) transform.Z = z.Value;
        if (yaw is not null) transform.Yaw = yaw.Value;
        if (pitch is not null) transform.Pitch = pitch.Value;

        // Player moved: subscribers mirror it to other players (visibility) and stream new
        // chunks as the player crosses into new columns.
        server.Events.Publish(new PlayerMoved(context));
    }

    void HandleInteract(NetClient client, InteractEntityC2S interact) {
        const int Attack = 1;
        string verb = interact.Type == Attack ? "attacked" : "interacted with";

        // Resolve who got hit, for a readable log (this is the player-visibility test signal).
        string target = $"entity {interact.TargetId}";
        foreach (var (_, context) in server.Players) {
            if (!context.World.Ecs.IsAlive(context.Entity)) continue;
            var np = context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity);
            if (np.EntityId == interact.TargetId) { target = np.Name; break; }
        }

        Log.LogInformation("#{Client} {Verb} {Target}", client.Id, verb, target);
    }

    /// <summary>The player swung their arm (a punch/attack) — animate it on every OTHER in-world client
    /// (each gets it in its own wire form via the generic <see cref="EntityAnimationS2C"/>).</summary>
    void HandleSwing(NetClient client, SwingArmC2S swing) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        int eid = context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity).EntityId;
        var animation = swing.Hand == 0 ? EntityAnimation.SwingMainArm : EntityAnimation.SwingOffArm;
        server.NetServer.Broadcast(new EntityAnimationS2C(eid, animation), c => c.InWorld && c.Id != client.Id);
    }

    /// <summary>The player toggled a shared-flags state (sneak/sprint) — we update the persisted flag set
    /// (so a joiner can be told the current state) and broadcast it to every OTHER in-world client as
    /// entity metadata. Each protocol maps the flags to its own wire form (modern flags+Pose; legacy flags).</summary>
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
        // The selected slot moved, so the main-hand item others see changes (handled by the subscriber).
        server.Events.Publish(new PlayerInventoryChanged(context));
    }

    void HandleCreativeSlot(NetClient client, SetCreativeModeSlotC2S creative) {
        if (!server.TryGetPlayer(client.Id, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        // A null stack = the client placed an item this server has no type for. We keep no such item, so the
        // client's predicted view is wrong; correct it, honouring the cursor where the player legitimately
        // grabbed an item in the same action. Warn as an OVERLAY so the creative client re-sending the
        // action doesn't spam chat.
        if (creative.Stack is not { } stack) {
            var current = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
            if (creative.Slot == CreativeDropSlot) {
                // Thrown from the cursor — the bad item never existed here; just clear the cursor.
                client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(current), default));
            } else if (ContainerManager.TryPlayerWindowToStorage(creative.Slot, out int swapIndex) && !current.Storage[swapIndex].IsEmpty) {
                // Swap onto a filled slot: the player grabbed the slot's item onto the cursor. Honour that —
                // empty the slot and resync with the grabbed item as the cursor — and drop only the bad item.
                var grabbed = current.Storage[swapIndex];
                current.Storage[swapIndex] = default;
                server.Events.Publish(new PlayerInventoryChanged(context)); // the (maybe-visible) slot changed
                client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(current), grabbed));
            } else {
                // Empty/unsupported slot: revert just that slot, leaving the cursor as the client has it (so a
                // duplicate of a preceding swap-reject can't wipe the grabbed item off the cursor).
                client.Send(new SetContainerSlotS2C(0, 0, creative.Slot, default));
            }
            client.Send(new SystemChatMessageS2C(
                new TextComponent("That item isn't available on this server.").SetColor(TextColor.Red), Overlay: true));
            return;
        }
        // Slot -1 is the creative "throw" (dropping out of the inventory): toss the carried item into the
        // world rather than storing it. The client only carries the item in the packet, not a real slot.
        // The codec already resolved the wire Slot into our ItemStack (including custom mod types).
        if (creative.Slot == CreativeDropSlot) {
            if (!stack.IsEmpty)
                context.World.TossItem(context.World.Ecs.Get<TransformEntityComponent>(context.Entity), stack);
            return;
        }
        if (!ContainerManager.TryPlayerWindowToStorage(creative.Slot, out int index))
            return; // crafting slot — ignored
        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        inventory.Storage[index] = stack; // an empty (non-null) stack here is a deliberate clear
        // If the edited slot is visible equipment (held/off-hand/armour), the subscriber updates others.
        server.Events.Publish(new PlayerInventoryChanged(context));
    }

    /// <summary>Creates and registers a block entity at a position (e.g. a chest's storage).</summary>
    static BlockEntity CreateBlockEntity(World world, Vector3i pos, BlockType type) {
        var entity = new BlockEntity(pos, type);
        world.SetBlockEntity(entity);
        return entity;
    }

    /// <summary>
    /// Broadcasts a block change to every IN-WORLD client — modern Play clients AND in-world legacy
    /// clients (which never enter the Play state). <see cref="BlockUpdateS2C"/> is generic, so each
    /// client's protocol encodes it in its own format (modern Block Update / legacy 0x35 Block Change).
    /// </summary>
    void BroadcastBlockChange(BlockUpdateS2C message) =>
        server.NetServer.Broadcast(message, c => c.InWorld);

    /// <summary>The cardinal a player-facing block (chest, …) should point — toward the player.</summary>
    static string FacingTowardPlayer(float yaw) {
        // Player yaw 0=south, 90=west, 180=north, 270=east; the block faces the player (opposite).
        int dir = (int)System.Math.Floor((yaw + 45f) / 90f) & 3;
        return dir switch { 0 => "north", 1 => "east", 2 => "south", _ => "west" };
    }

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
