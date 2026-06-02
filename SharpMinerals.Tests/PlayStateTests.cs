using System.Collections.Concurrent;
using Arch.Core;
using Brigadier.NET.Builder;
using SharpMinerals;
using SharpMinerals.Blocks;
using SharpMinerals.Commands;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Modding;
using SharpMinerals.Network;
using NuGet.Versioning;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Network.Messages;
using SharpMinerals.Chat;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Network.Protocols.JE763.Codecs;
using SharpMinerals.Persistence;
using Xunit;
using World = SharpMinerals.Level.World;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Tests;

/// <summary>
/// In-process verification of the play state: flat generation, block break/place, item
/// drops, codec round-trips, and the login → dig → place → container → pickup handler flow
/// driven through an in-memory transport. Ported from the old <c>dotnet run -- selftest</c>.
/// </summary>
public class PlayStateTests {
    // The JE763 type mapper, now exposed per-protocol (was a static class).
    static readonly ITypeMapper Types = new TypeMapperJE763();

    // ── Flat generation ────────────────────────────────────────────────────
    [Fact]
    public void FlatGeneration() {
        var gen = new World("gen", new FlatChunkGenerator());
        Assert.True(gen.GetBlock(new Vector3i(0, 0, 0)) == BlockRegistry.Bedrock, "flat: bedrock at y=0");
        Assert.True(gen.GetBlock(new Vector3i(0, 2, 0)) == BlockRegistry.Dirt, "flat: dirt at y=2");
        Assert.True(gen.GetBlock(new Vector3i(0, 4, 0)) == BlockRegistry.GrassBlock, "flat: grass at y=4");
        Assert.True(gen.GetBlock(new Vector3i(0, 5, 0)).IsAir, "flat: air at y=5");
        Assert.True(gen.GetBlock(new Vector3i(-1, 0, -33)) == BlockRegistry.Bedrock, "flat: works at negative coords");
    }

    // ── Block break + drop, then placement ──────────────────────────────────
    [Fact]
    public void BlockBreakPlaceAndDrops() {
        var gen = new World("gen", new FlatChunkGenerator());

        int before = DropCount(gen);
        var broken = gen.BreakBlock(new Vector3i(5, 4, 5));
        Assert.True(broken == BlockRegistry.GrassBlock, "break: returned the grass block");
        Assert.True(gen.GetBlock(new Vector3i(5, 4, 5)).IsAir, "break: space is now air");
        Assert.True(DropCount(gen) == before + 1, "break: spawned one drop entity");
        Assert.True(gen.BreakBlock(new Vector3i(5, 50, 5)).IsAir, "break: air yields nothing");

        Assert.True(gen.PlaceBlock(new Vector3i(5, 5, 5), BlockRegistry.Stone), "place: into air succeeds");
        Assert.True(gen.GetBlock(new Vector3i(5, 5, 5)) == BlockRegistry.Stone, "place: block was set");
        Assert.True(!gen.PlaceBlock(new Vector3i(5, 5, 5), BlockRegistry.Dirt), "place: into a solid fails");
    }

    // ── Position packing ────────────────────────────────────────────────────
    [Fact]
    public void PositionPacking() {
        foreach (var p in new[] {
            new Vector3i(0, 0, 0), new Vector3i(1, 2, 3),
            new Vector3i(-30_000_000, -2000, 30_000_000),
        }) {
            using var ms = new MinecraftStream(new MemoryStream());
            ms.WritePosition(p.X, p.Y, p.Z);
            ms.Position = 0;
            var (x, y, z) = ms.ReadPosition();
            Assert.True(x == p.X && y == p.Y && z == p.Z, $"position packs {p}");
        }
    }

    // ── Codec round-trips ───────────────────────────────────────────────────
    [Fact]
    public void CodecRoundTrips() {
        var protocol = new ProtocolJE763();
        Assert.True(RoundTrip(protocol, ConnectionState.Handshaking,
            new HandshakeC2S(763, "localhost", 25565, 2)), "round-trip Handshake");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new PlayerActionC2S(2, new Vector3i(10, -5, 20), 1, 7)), "round-trip PlayerAction");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new UseItemOnC2S(0, new Vector3i(1, 2, 3), 1, 0.5f, 0.25f, 0.75f, false, 9)), "round-trip UseItemOn");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new SetPlayerPositionC2S(1.5, 64.0, -3.5, true)), "round-trip SetPlayerPosition");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new KeepAliveC2S(123_456_789L)), "round-trip KeepAlive");
    }

    // ── Block-state + item mapping (no server needed) ───────────────────────
    [Fact]
    public void StateAndItemMapping() {
        Assert.True(Types.StateId(new BlockState(BlockRegistry.Chest).Set(State.Facing, "east")) == 2973,
            "state: facing maps to vanilla id (chest east = 2955 + 3*6)");
        Assert.True(Types.StateId(new BlockState(BlockRegistry.Wool).Set(State.Color, "red")) == 2061,
            "state: wool colour override (red = 2047 + 14)");
        Assert.True(Types.FromVanillaItem(194).State?.Get(State.Color) == 14,
            "item: vanilla wool id → coloured stack (red 194 → colour 14)");
        Assert.True(Types.ItemId(Types.FromVanillaItem(194)) == 194,
            "item: coloured wool stack round-trips its vanilla id (red 194)");

        var woolInv = new InventoryEntityComponent();
        woolInv.Add(Types.FromVanillaItem(194)); // red wool
        woolInv.Add(Types.FromVanillaItem(180)); // white wool
        Assert.True(woolInv.Main(0).State?.Get(State.Color) == 14 && woolInv.Main(1).State?.Get(State.Color) == 0,
            "pickup: different wool colours take separate slots");
        woolInv.Add(Types.FromVanillaItem(194)); // another red merges with the first
        Assert.True(woolInv.Main(0).Count == 2 && woolInv.Main(1).Count == 1, "pickup: same wool colour stacks");
    }

    // ── Handler flow over an in-memory transport ────────────────────────────
    [Fact]
    public void HandlerFlow() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "self-test", MaxPlayers = 20, TicksPerSecond = 20,
        });
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);

        // Login.
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Assert.True(client.State == ConnectionState.Play, "login: switched to Play");
        Assert.True(client.Sent.Any(m => m is LoginSuccessS2C), "login: sent LoginSuccess");
        Assert.True(client.Sent.Any(m => m is JoinGameS2C), "login: sent JoinGame");
        Assert.True(client.Sent.Any(m => m is SetCenterChunkS2C), "login: sent Set Center Chunk");
        Assert.True(client.Sent.OfType<ChunkDataS2C>().Any(), "login: streamed chunk data");
        Assert.True(client.Sent.Any(m => m is SetHealthS2C), "login: sent SetHealth");
        Assert.True(server.PlayerCount == 1, "login: player spawned");
        Assert.True(
            ServerPacketHandler.OfflineUuid("Steve") == ServerPacketHandler.OfflineUuid("Steve") &&
            ServerPacketHandler.OfflineUuid("Steve") != Guid.Empty,
            "login: offline UUID is deterministic + non-empty");

        // Digging (creative instant break, status 0).
        client.Sent.Clear();
        capture.Broadcasts.Clear();
        var digPos = new Vector3i(0, 4, 0);
        Assert.True(server.DefaultWorld.GetBlock(digPos) == BlockRegistry.GrassBlock, "dig: target starts as grass");
        handler.Handle(client, new PlayerActionC2S(0, digPos, 1, 42));
        server.Events.DrainDeferred(); // dig is deferred to the tick's single-writer phase
        Assert.True(server.DefaultWorld.GetBlock(digPos).IsAir, "dig: block removed");
        Assert.True(client.Sent.Any(m => m is AckBlockChangeS2C a && a.Sequence == 42), "dig: acknowledged sequence");
        Assert.True(
            capture.Broadcasts.Any(m => m is BlockUpdateS2C b && b.Position == digPos && b.Block == BlockRegistry.Air),
            "dig: BlockUpdate broadcast");
        server.AnnounceSystems(); // the pre-physics announce broadcasts new drops' SpawnEntity
        Assert.True(capture.Broadcasts.Any(m => m is SpawnEntityS2C), "dig: drop announced (SpawnEntity)");

        // Placement (held item is Stone; placing on the top face of a grass block).
        capture.Broadcasts.Clear();
        var placeOn = new Vector3i(2, 4, 2);
        var placedAt = new Vector3i(2, 5, 2);
        handler.Handle(client, new UseItemOnC2S(0, placeOn, (int)BlockFace.Top, 0.5f, 1f, 0.5f, false, 43));
        server.Events.DrainDeferred(); // placement is deferred to the tick's single-writer phase
        Assert.True(server.DefaultWorld.GetBlock(placedAt) == BlockRegistry.Stone, "place: stone placed above clicked face");
        Assert.True(
            capture.Broadcasts.Any(m => m is BlockUpdateS2C b && b.Position == placedAt && b.Block == BlockRegistry.Stone),
            "place: BlockUpdate broadcast");

        // ── Containers: open a chest, move an item in, sync to a second viewer ──
        client.Sent.Clear();
        var chestEntity = new BlockEntity(new Vector3i(10, 5, 10), BlockRegistry.Chest);
        server.DefaultWorld.SetBlockEntity(chestEntity);
        server.Containers.Open(server, client.Id, chestEntity);
        Assert.True(client.Sent.OfType<OpenScreenS2C>().Any(), "container: open sent OpenScreen");
        Assert.True(client.Sent.OfType<SetContainerContentS2C>().Any(), "container: open sent Content");

        int win = client.Sent.OfType<OpenScreenS2C>().First().WindowId;
        // Pick up the hotbar stone (chest-window slot 54 = Main(0)) then drop it into chest slot 0.
        server.Containers.OnClick(server, client.Id, new ClickContainerC2S(win, 0, 54, 0, 0));
        server.Containers.OnClick(server, client.Id, new ClickContainerC2S(win, 1, 0, 0, 0));
        var chestInv = chestEntity.Get<InventoryComponent>();
        Assert.True(!chestInv[0].IsEmpty && chestInv[0].Type == BlockRegistry.Stone, "container: stone moved into chest");

        // A second viewer of the same chest is synced when the first viewer clicks.
        var viewer = new CaptureNetClient(2, protocol) { State = ConnectionState.Play };
        capture.Register(viewer);
        server.AddPlayer(viewer, "Steve2", ServerPacketHandler.OfflineUuid("Steve2"));
        server.Containers.Open(server, viewer.Id, chestEntity);
        viewer.Sent.Clear();
        server.Containers.OnClick(server, client.Id, new ClickContainerC2S(win, 2, 0, 0, 0)); // #1 picks the stack back up
        Assert.True(viewer.Sent.OfType<SetContainerContentS2C>().Any(), "container: second viewer synced");
        server.RemovePlayer(viewer.Id);

        // ── Item pickup: a dropped stack near the player is collected via collision ──
        var dropEntity = server.DefaultWorld.SpawnDroppedItem(new Vector3i(0, 5, 0), new ItemStack(BlockRegistry.Cobblestone, 1));
        server.DefaultWorld.Ecs.Get<VelocityEntityComponent>(dropEntity) = new VelocityEntityComponent(0, 0, 0); // pin it under the player (no random scatter)
        server.AnnounceSystems();                    // assign its network id (pickup ignores un-announced drops)
        for (int i = 0; i < 12; i++) server.DefaultWorld.Tick(); // age past pickup delay, settle, ItemPickupSystem collects it
        server.FlushSystems();                                   // project the pickup (collect animation + removal)
        Assert.True(!server.DefaultWorld.Ecs.IsAlive(dropEntity), "pickup: drop entity removed");
        server.TryGetPlayer(client.Id, out var context);
        var pickInv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        bool hasCobble = false;
        for (int s = 0; s < InventoryEntityComponent.MainSize; s++)
            if (pickInv.Main(s).Type == BlockRegistry.Cobblestone) hasCobble = true;
        Assert.True(hasCobble, "pickup: item added to inventory");
        int pickerNetId = server.DefaultWorld.Ecs.Get<NetPlayerEntityComponent>(context.Entity).EntityId;
        Assert.True(
            capture.Broadcasts.Any(m => m is CollectItemS2C c && c.CollectorEntityId == pickerNetId && c.PickupItemCount == 1),
            "pickup: collect-item animation broadcast (collector + count)");

        // ── Block state: set + read a chest's facing; break clears it ──
        var statePos = new Vector3i(7, 5, 7);
        server.DefaultWorld.SetBlock(statePos, BlockRegistry.Chest);
        server.DefaultWorld.SetBlockState(statePos, new BlockState(BlockRegistry.Chest).Set(State.Facing, "east"));
        Assert.True(
            server.DefaultWorld.GetBlockState(statePos)?.Get(State.Facing) == State.Facing.IndexOf("east"),
            "state: facing stored + read back");
        server.DefaultWorld.BreakBlock(statePos);
        Assert.True(server.DefaultWorld.GetBlockState(statePos) is null, "state: cleared on break");

        // Disconnect cleanup.
        server.RemovePlayer(client.Id);
        Assert.True(server.PlayerCount == 0, "disconnect: player despawned");
    }

    // ── Drops: a self-dropping stateful block drops an ItemStack carrying its state ──
    [Fact]
    public void WoolDropsAsColouredStack() {
        var world = new World("drop", new FlatChunkGenerator());
        var pos = new Vector3i(3, 6, 3);
        world.SetBlock(pos, BlockRegistry.Wool);
        world.SetBlockState(pos, new BlockState(BlockRegistry.Wool).Set(State.Color, "red"));

        world.BreakBlock(pos);

        ItemStack? dropped = null;
        world.Ecs.Query(in new QueryDescription().WithAll<PickupEntityComponent>(),
            (ref PickupEntityComponent d) => dropped = d.Stack);
        Assert.True(dropped is { } ds && ds.Type == BlockRegistry.Wool && ds.State?.Get(State.Color) == 14,
            "drop: wool drops as a coloured ItemStack");
    }

    // ── Drops: the item's fresh pop velocity is delivered via Set Entity Velocity (announced pre-physics) ──
    [Fact]
    public void DroppedItemSpawnCarriesPopVelocity() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });

        // Break a block → drop spawns with the upward pop (DropVelocity Y = 0.2). Announce BEFORE any
        // physics tick, so the velocity is the un-decayed spawn value.
        server.DefaultWorld.BreakBlock(new Vector3i(0, 4, 0));
        server.AnnounceSystems();

        // Velocity is delivered by the explicit Set Entity Velocity packet (the 1.20.1 client ignores
        // the spawn-packet velocity for items); the spawn packet's velocity is deliberately zeroed so it
        // can't double-apply. 0.2 blocks/tick × 8000 = 1600.
        var spawn = capture.Broadcasts.OfType<SpawnEntityS2C>().First();
        Assert.Equal(0, spawn.VelocityY);
        var vel = capture.Broadcasts.OfType<SetEntityVelocityS2C>().First();
        Assert.Equal(spawn.EntityId, vel.EntityId);
        Assert.True(vel.VelocityY > 1000, $"item pop velocity sent (VelocityY={vel.VelocityY})");
    }

    // ── Falling blocks: sand over air detaches into a falling_block entity, falls, and re-places ──
    [Fact]
    public void SandFallsAndRePlacesOnLanding() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });
        var world = server.DefaultWorld;

        // A lone stone platform high above the flat terrain, with sand a few cells above it over air.
        var floor = new Vector3i(20, 100, 20);
        var sand = new Vector3i(20, 104, 20);
        var landed = new Vector3i(20, 101, 20); // rests on top of the floor
        world.SetBlock(floor, BlockRegistry.Stone);
        world.SetBlock(sand, BlockRegistry.Sand);

        var fallingQuery = new QueryDescription().WithAll<FallingBlockEntityComponent>();

        // Air beneath the sand → it detaches: the source cell clears and one falling entity appears.
        SharpMinerals.Level.Systems.FallingBlockSystem.TryStartFalling(server, world, sand);
        Assert.True(world.GetBlock(sand).IsAir, "fall: the sand's source cell is cleared");
        Assert.Equal(1, world.Ecs.CountEntities(in fallingQuery));

        // Announced (pre-physics) as a falling_block carrying its block (Data = the per-protocol state id).
        server.AnnounceSystems();
        var spawn = capture.Broadcasts.OfType<SpawnEntityS2C>().First();
        Assert.Equal(EntityRegistry.FallingBlock, spawn.Type);
        Assert.Equal(BlockRegistry.Sand, spawn.BlockData);

        // Landing happens inside the world tick (FallingBlockSystem fires the block's IOnLand reaction and
        // despawns the entity, recording the landing); its Flush then projects the block update + removal.
        for (int i = 0; i < 200 && world.Ecs.CountEntities(in fallingQuery) != 0; i++)
            world.Tick();
        server.FlushSystems();

        Assert.Equal(0, world.Ecs.CountEntities(in fallingQuery));
        Assert.Equal(BlockRegistry.Sand, world.GetBlock(landed));
        Assert.True(world.GetBlock(sand).IsAir, "fall: the original cell stays air after landing");
        Assert.Contains(capture.Broadcasts.OfType<BlockUpdateS2C>(),
            b => b.Position.Equals(landed) && b.Block == BlockRegistry.Sand);
        Assert.Contains(capture.Broadcasts.OfType<RemoveEntitiesS2C>(), r => r.EntityIds.Contains(spawn.EntityId));
    }

    // ── Mods: the ModLoader discovers a compiled-in mod and its OnServerStarted registers a command ──
    [Fact]
    public void ModLoaderLoadsTestModAndRegistersItsCommand() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });

        // Discover the test-harness mod from its (compiled-in) assembly — the same LoadFrom path the CLI
        // and the real-client fixture use.
        var loader = new ModLoader();
        loader.LoadFrom(typeof(SharpMinerals.TestMod.TestMod).Assembly);
        Assert.Single(loader.Mods);
        Assert.Equal("sharpminerals_test", loader.Mods[0].Info.ModId);

        // OnServerStarted registers /test on the dispatcher; running it (no @id) broadcasts to play clients.
        loader.StartAll(server);
        server.Sender.RunCommand("test hello world");
        Assert.Contains(capture.Broadcasts.OfType<TestCommandS2C>(), m => m.Command == "hello world");
    }

    sealed class VersionProbeMod : Mod { }

    // ── A mod is loaded only if its declared target server version is compatible (and its own is valid) ──
    [Fact]
    public void ModLoaderGatesOnTargetServerVersion() {
        var loader = new ModLoader { ServerVersion = SemanticVersion.Parse("0.1.0") };

        Assert.True(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("compat", "1.0.0") { TargetServerVersion = "0.1.0" }));
        Assert.False(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("too_new", "1.0.0") { TargetServerVersion = "0.2.0" }));  // needs newer server
        Assert.False(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("wrong_major", "1.0.0") { TargetServerVersion = "1.0.0" })); // major mismatch
        Assert.False(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("bad_version", "not-semver")));                            // invalid own version

        Assert.Single(loader.Mods);                                     // only the compatible mod loaded
        Assert.Equal("compat", loader.Mods[0].Info.ModId);
        Assert.Equal(SemanticVersion.Parse("1.0.0"), loader.Mods[0].Version); // parsed version exposed on the mod
    }

    // ── Custom objects: a mod-added type renders as the fallback item but carries differentiating NBT ──
    [Fact]
    public void CustomItemCarriesNameAndIdentityNbt() {
        var custom = ItemRegistry.Register("sm_custom_test"); // not a vanilla name → mapper treats it as custom
        var mapper = new TypeMapperJE763();
        Assert.True(mapper.IsCustom(custom));
        Assert.False(mapper.IsCustom(BlockRegistry.Stone));

        byte[] Encode(ItemStack stack) {
            using var ms = new System.IO.MemoryStream();
            var s = new MinecraftStream(ms, leaveOpen: true) { Types = mapper };
            SlotWire.WriteStack(s, stack);
            return ms.ToArray();
        }

        var customText = System.Text.Encoding.UTF8.GetString(Encode(new ItemStack(custom)));
        var stoneText = System.Text.Encoding.UTF8.GetString(Encode(new ItemStack(BlockRegistry.Stone)));

        // The custom item gets a translatable display name (with a humanised fallback) and an identity
        // marker that keeps the client from stacking it with the fallback item or other custom types.
        Assert.Contains("item.sharpminerals.sm_custom_test", customText);
        Assert.Contains("Sm Custom Test", customText);
        Assert.Contains("SharpMineralsType", customText);
        // A vanilla item is a plain slot — no display name, no marker (so it stacks normally).
        Assert.DoesNotContain("SharpMineralsType", stoneText);
    }

    // ── Custom objects: the identity survives the wire round-trip when the client echoes the slot back ──
    [Fact]
    public void CustomItemIdentitySurvivesSlotRoundTrip() {
        var custom = ItemRegistry.Register("sm_roundtrip_item");
        var mapper = new TypeMapperJE763();

        using var ms = new System.IO.MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true) { Types = mapper };
        SlotWire.WriteStack(w, new ItemStack(custom, 3)); // server → client: fallback id + count + NBT marker

        ms.Position = 0;
        var r = new MinecraftStream(ms, leaveOpen: true) { Types = mapper };
        var restored = SlotWire.ReadStack(r); // client echo decoded back to our internal ItemStack
        Assert.NotNull(restored);
        Assert.Equal(custom, restored!.Value.Type); // recovered the custom type, not the fallback (stone)
        Assert.Equal(3, restored.Value.Count);
    }

    // ── Creative: an item this server can't represent is reported + corrected, honouring the cursor ──
    [Fact]
    public void CreativeSlotWithUnknownItemWarnsAndCorrectsClient() {
        var protocol = new ProtocolJE763();

        // Decode side: a present wire slot whose id maps to no SharpMinerals type reads back as null.
        using (var ms = new System.IO.MemoryStream()) {
            var w = new MinecraftStream(ms, leaveOpen: true) { Types = protocol.Types };
            w.WriteBool(true); w.WriteVarInt(999); w.WriteByte2(1); w.WriteUByte(0x00); // present, unknown id, no NBT
            ms.Position = 0;
            var r = new MinecraftStream(ms, leaveOpen: true) { Types = protocol.Types };
            Assert.Null(SlotWire.ReadStack(r)); // unrepresentable → null
        }

        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Assert.True(server.TryGetPlayer(client.Id, out var context));
        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);

        // Swap onto a FILLED slot: the invalid item is rejected, but the stone the player grabbed in the same
        // action is kept on the cursor (slot emptied), so they don't lose it. Window slot 36 = hotbar 0.
        inv.Main(0) = new ItemStack(BlockRegistry.Stone, 5);
        client.Sent.Clear();
        handler.Handle(client, new SetCreativeModeSlotC2S(36, null));
        server.Events.DrainDeferred(); // creative slot is deferred to the tick

        Assert.Contains(client.Sent, m => m is SystemChatMessageS2C s && s.Overlay); // overlay warning forwarded
        var resync = client.Sent.OfType<SetContainerContentS2C>().Last();
        Assert.Equal(BlockRegistry.Stone, resync.Carried.Type); // the grabbed item stays on the cursor
        Assert.True(resync.Slots[36].IsEmpty);                  // ...and the slot it came from is now empty
        Assert.True(inv.Main(0).IsEmpty);                       // server agrees: the slot was emptied (item on cursor)

        // Into an EMPTY slot: just revert that slot, WITHOUT touching the cursor (a duplicate of the swap
        // above must not wipe the grabbed item) — a single-slot correction, no content/cursor resync.
        Assert.True(inv.Main(2).IsEmpty); // hotbar 2 starts empty (player spawns with items only in 0–1)
        client.Sent.Clear();
        handler.Handle(client, new SetCreativeModeSlotC2S(38, null)); // window slot 38 = hotbar 2 (empty)
        server.Events.DrainDeferred();
        var slotFix = client.Sent.OfType<SetContainerSlotS2C>().Single();
        Assert.Equal(38, slotFix.Slot);
        Assert.True(slotFix.Data.IsEmpty);
        Assert.DoesNotContain(client.Sent, m => m is SetContainerContentS2C); // cursor left alone
    }

    // ── Regression: a creative Set-Creative-Slot decodes through the protocol (the type mapper must be ──
    // available on the DECODE stream, not just encode — else ReadStack NRE'd on every creative add/clone). ──
    [Fact]
    public void CreativeSlotPacketDecodesThroughProtocol() {
        var protocol = new ProtocolJE763();

        // The wire body a creative client sends: packet id + slot(short) + Slot.
        using var bodyMs = new System.IO.MemoryStream();
        var bw = new MinecraftStream(bodyMs, leaveOpen: true) { Types = protocol.Types };
        bw.WriteVarInt(0x2B); // Sb.SetCreativeModeSlot
        bw.WriteShort(36);
        SlotWire.WriteStack(bw, new ItemStack(BlockRegistry.Stone, 5));
        var body = bodyMs.ToArray();

        // Frame it (VarInt length + body) and decode it through the protocol — the production path whose
        // stream had no Types on decode, so a creative add/clone crashed with NRE in SlotWire.ReadStack.
        using var frameMs = new System.IO.MemoryStream();
        var fw = new MinecraftStream(frameMs, leaveOpen: true);
        fw.WriteVarInt(body.Length);
        fw.Write(body, 0, body.Length);
        frameMs.Position = 0;

        var msg = protocol.ReadMessage(new MinecraftStream(frameMs, leaveOpen: true),
            ConnectionState.Play, PacketDirection.Serverbound);
        var creative = Assert.IsType<SetCreativeModeSlotC2S>(msg);
        Assert.Equal(36, creative.Slot);
        Assert.NotNull(creative.Stack);
        Assert.Equal(BlockRegistry.Stone, creative.Stack!.Value.Type);
        Assert.Equal(5, creative.Stack.Value.Count);
    }

    // ── Chunk streaming includes block entities (a chest renders on load, not only after an update) ──
    [Fact]
    public void ChunkPacketIncludesBlockEntities() {
        var world = new World("be", new FlatChunkGenerator());
        var pos = new Vector3i(3, 70, 5);
        // Just the block — NO BlockEntity instance (an unopened chest). The packet entry is derived from the
        // block state, so it must still be sent (this is exactly the "some chests don't render" case).
        world.SetBlock(pos, BlockRegistry.Chest);

        var packet = ChunkSerializer.Build(Types, world, 0, 0);
        var s = new MinecraftStream(new System.IO.MemoryStream(packet.Payload, writable: false));
        s.ReadInt(); s.ReadInt();                                  // chunkX, chunkZ
        SharpMinerals.Network.Nbt.NbtReader.ReadItemNbt(s);        // consume the heightmaps NBT
        s.ReadBytes(s.ReadVarInt());                               // skip the sections blob

        Assert.Equal(1, s.ReadVarInt());                          // one block entity in the column
        Assert.Equal((3 << 4) | 5, s.ReadUByte());                // packed local XZ
        Assert.Equal((short)70, s.ReadShort());                   // world Y
        Assert.Equal(1, s.ReadVarInt());                          // minecraft:chest block-entity-type id
    }

    // ── Tab-list header/footer (0x65) encodes as two JSON components and honours the audience predicate ──
    [Fact]
    public void TabListHeaderFooterEncodesAndRespectsAudience() {
        var protocol = new ProtocolJE763();
        var header = new TextComponent("Top").SetColor(TextColor.Gold);
        var footer = new TextComponent("Bottom");

        // Wire: packet id 0x65, then the header and footer as JSON chat strings.
        var bytes = protocol.EncodePayload(new PlayerListHeaderFooterS2C(header, footer));
        var s = new MinecraftStream(new System.IO.MemoryStream(bytes, writable: false));
        Assert.Equal(0x65, s.ReadVarInt());
        Assert.Equal(header.ToString(), s.ReadString());
        Assert.Equal(footer.ToString(), s.ReadString());

        // The server method sends only to the clients the predicate selects.
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 8, TicksPerSecond = 20,
        });
        var c1 = new CaptureNetClient(1, protocol) { State = ConnectionState.Play };
        var c2 = new CaptureNetClient(2, protocol) { State = ConnectionState.Play };
        capture.Register(c1);
        capture.Register(c2);

        server.SetTabListHeaderFooter(header, footer, c => c.Id == 1);
        Assert.Single(c1.Sent.OfType<PlayerListHeaderFooterS2C>());
        Assert.Empty(c2.Sent.OfType<PlayerListHeaderFooterS2C>());
    }

    // ── /clear empties the player's inventory and resyncs the window ──
    [Fact]
    public void ClearCommandEmptiesInventoryAndResyncs() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });
        server.CommandDispatcher.RegisterClear();
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Assert.True(server.TryGetPlayer(client.Id, out var context));

        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        inv.Main(0) = new ItemStack(BlockRegistry.Stone, 10);
        inv.Main(5) = new ItemStack(BlockRegistry.Dirt, 3);
        inv.Offhand = new ItemStack(BlockRegistry.Cobblestone, 1);
        client.Sent.Clear();

        var sender = server.DefaultWorld.Ecs.Get<SenderEntityComponent>(context.Entity);
        _ = server.CommandDispatcher.ExecuteAsync(sender, "clear", client); // synchronous-bodied

        Assert.True(inv.Main(0).IsEmpty && inv.Main(5).IsEmpty && inv.Offhand.IsEmpty, "every slot cleared");
        // The emptied window is pushed back to the client so its view clears.
        var resync = client.Sent.OfType<SetContainerContentS2C>().Last();
        Assert.All(resync.Slots, s => Assert.True(s.IsEmpty));
        Assert.True(resync.Carried.IsEmpty);
    }

    // ── /give adds an item (by registry name) to the player's inventory and resyncs the window ──
    [Fact]
    public void GiveCommandAddsItemAndResyncs() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });
        server.CommandDispatcher.RegisterGive();
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Assert.True(server.TryGetPlayer(client.Id, out var context));
        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        var sender = server.DefaultWorld.Ecs.Get<SenderEntityComponent>(context.Entity);
        client.Sent.Clear();

        _ = server.CommandDispatcher.ExecuteAsync(sender, "give cobblestone 10", client); // synchronous-bodied

        int total = 0;
        for (int i = 0; i < InventoryEntityComponent.MainSize; i++)
            if (inv.Main(i).Type == BlockRegistry.Cobblestone) total += inv.Main(i).Count;
        Assert.Equal(10, total);                                              // the 10 cobblestone landed in the inventory
        Assert.NotEmpty(client.Sent.OfType<SetContainerContentS2C>());        // window resynced to the client

        // An unknown item is rejected without touching the inventory.
        client.Sent.Clear();
        _ = server.CommandDispatcher.ExecuteAsync(sender, "give not_a_real_item", client);
        Assert.Empty(client.Sent.OfType<SetContainerContentS2C>());
    }

    // ── InventoryComponent.Add splits a large count across slots, capped at the item's max stack size ──
    [Fact]
    public void InventoryAddRespectsMaxStackSize() {
        var inv = new InventoryComponent(InventoryEntityComponent.MainSize);

        // 100 stone (max 64) → 64 + 36 across two slots, nothing left over (the old Add over-stuffed one slot).
        Assert.True(inv.Add(new ItemStack(BlockRegistry.Stone, 100)).IsEmpty);
        Assert.Equal(64, inv[0].Count);
        Assert.Equal(36, inv[1].Count);

        // Adding more tops up the partial slot first, then spills into a fresh one.
        Assert.True(inv.Add(new ItemStack(BlockRegistry.Stone, 30)).IsEmpty);
        Assert.Equal(64, inv[1].Count); // 36 → 64
        Assert.Equal(2, inv[2].Count);  // the remaining 2

        // When the range is full, the overflow is returned rather than over-stacked.
        var small = new InventoryComponent(1);
        var leftover = small.Add(new ItemStack(BlockRegistry.Stone, 100));
        Assert.Equal(64, small[0].Count);
        Assert.Equal(36, leftover.Count);
    }

    // ── Equipment: held item syncs as Set Equipment; off-hand never reaches a legacy client ─────────
    [Fact]
    public void EquipmentSyncsAndOffhandSkipsLegacy() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Assert.True(server.TryGetPlayer(client.Id, out var context));
        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        int eid = server.DefaultWorld.Ecs.Get<NetPlayerEntityComponent>(context.Entity).EntityId;

        // Selecting a hotbar slot broadcasts the held item to others as main-hand equipment.
        inv.Main(2) = new ItemStack(BlockRegistry.Cobblestone, 1);
        capture.Broadcasts.Clear();
        handler.Handle(client, new SetHeldItemC2S(2));
        server.Events.DrainDeferred(); // the held-item change is deferred to the tick
        server.FlushSystems();         // equipment-visibility diff broadcasts the changed slot
        Assert.Equal(2, inv.SelectedSlot);
        var held = capture.Broadcasts.OfType<SetEquipmentS2C>().Single();
        Assert.Equal(eid, held.EntityId);
        Assert.Equal(EquipmentSlot.MainHand, held.Slot);
        Assert.Equal(BlockRegistry.Cobblestone, held.Item.Type);

        // A container click that moves the held item updates the hand for others — here, picking the held
        // cobblestone up off hotbar slot 2 (chest-window slot 54 + 2) onto the cursor empties the hand.
        var chest = new BlockEntity(new Vector3i(10, 5, 10), BlockRegistry.Chest);
        server.DefaultWorld.SetBlockEntity(chest);
        server.Containers.Open(server, client.Id, chest);
        int win = client.Sent.OfType<OpenScreenS2C>().Last().WindowId;
        capture.Broadcasts.Clear();
        server.Containers.OnClick(server, client.Id, new ClickContainerC2S(win, 0, 56, 0, 0)); // left-click held slot → cursor
        server.FlushSystems(); // equipment-visibility diff broadcasts the now-empty hand
        Assert.True(inv.Held.IsEmpty, "container: held item moved onto the cursor");
        var cleared = capture.Broadcasts.OfType<SetEquipmentS2C>().Single();
        Assert.Equal(EquipmentSlot.MainHand, cleared.Slot);
        Assert.True(cleared.Item.IsEmpty, "container click cleared the hand for other players");

        // Modern wire: Set Equipment 0x55 = entity id, slot byte (Helmet=5, top bit clear), then the item Slot.
        var payload = protocol.EncodePayload(new SetEquipmentS2C(eid, EquipmentSlot.Helmet, new ItemStack(BlockRegistry.Cobblestone, 1)));
        using (var ms = new MinecraftStream(new MemoryStream(payload, writable: false))) {
            Assert.Equal(0x55, ms.ReadVarInt());
            Assert.Equal(eid, ms.ReadVarInt());
            Assert.Equal((byte)5, ms.ReadUByte());
            Assert.True(ms.ReadSlotLite() is { } slot && slot.Count == 1, "modern: item Slot encoded with count 1");
        }

        // Legacy wire: 1.5.2 Entity Equipment 0x05 = int entity id, short slot (Helmet → 4), then a legacy Slot.
        var je61 = new ProtocolJE61();
        var framed = je61.Frame(new SetEquipmentS2C(eid, EquipmentSlot.Helmet, new ItemStack(BlockRegistry.Cobblestone, 1)));
        Assert.Equal((byte)0x05, framed[0]);
        using (var ls = new MinecraftStream(new MemoryStream(framed[1..], writable: false))) {
            Assert.Equal(eid, ls.ReadInt());
            Assert.Equal((short)4, ls.ReadShort());
            var (id, count, _) = ls.ReadLegacySlot();
            Assert.True(id != -1 && count == 1, "legacy: item Slot encoded with count 1");
        }

        // The legacy encoder genuinely cannot represent the off-hand (added in 1.9) — so it MUST be filtered.
        Assert.Throws<NotSupportedException>(() =>
            je61.Frame(new SetEquipmentS2C(eid, EquipmentSlot.OffHand, new ItemStack(BlockRegistry.Cobblestone, 1))));

        // ...and it is: a legacy in-world client is ALSO in the Play state, so the gate is protocol VERSION.
        var legacyClient = new CaptureNetClient(2, je61) { State = ConnectionState.Play };
        var modernClient = new CaptureNetClient(3, protocol) { State = ConnectionState.Play };
        Assert.False(PlayerVisibility.CanSeeOffhand(legacyClient), "off-hand stays off the legacy wire");
        Assert.True(PlayerVisibility.CanSeeOffhand(modernClient), "modern client renders off-hand");
    }

    // ── World switch: a connected player moves to another world, keeping inventory + network id ─────────
    [Fact]
    public void SwitchWorldMovesPlayerAndPreservesInventory() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Assert.True(server.TryGetPlayer(client.Id, out var before));
        var oldWorld = before.World;
        before.World.Ecs.Get<InventoryEntityComponent>(before.Entity).Main(5) = new ItemStack(BlockRegistry.Cobblestone, 3);
        int eid = before.World.Ecs.Get<NetPlayerEntityComponent>(before.Entity).EntityId;

        client.Sent.Clear();
        var target = server.GetOrCreateWorld("arena", static (name, server) => new World("arena"));
        server.SwitchWorld(client.Id, target);

        // The context now points at the target world, with a live entity reusing the same network id.
        Assert.True(server.TryGetPlayer(client.Id, out var after));
        Assert.Same(target, after.World);
        Assert.NotSame(oldWorld, after.World);
        Assert.True(after.World.Ecs.IsAlive(after.Entity), "entity alive in the new world");
        Assert.False(oldWorld.Ecs.IsAlive(before.Entity), "old entity despawned");
        Assert.Equal(eid, after.World.Ecs.Get<NetPlayerEntityComponent>(after.Entity).EntityId);
        Assert.Equal(BlockRegistry.Cobblestone, after.World.Ecs.Get<InventoryEntityComponent>(after.Entity).Main(5).Type);
        // The client was told to respawn into the new dimension and got its fresh chunks.
        var respawn = Assert.IsType<RespawnS2C>(client.Sent.First(m => m is RespawnS2C));
        Assert.Contains(client.Sent, m => m is ChunkDataS2C);
        // The Respawn must carry the TARGET world's key, and it must differ from the world the player came
        // from — a same-key Respawn doesn't reload the 1.20.1 client, so the old world's entities would linger.
        Assert.Equal(target.Name, respawn.WorldName);
        Assert.NotEqual(oldWorld.Name, respawn.WorldName);
        // Switching to the world it's already in is a no-op.
        client.Sent.Clear();
        server.SwitchWorld(client.Id, target);
        Assert.DoesNotContain(client.Sent, m => m is RespawnS2C);
    }

    // ── Persistence: state survives a disconnect/reconnect (in-memory store) ────────
    [Fact]
    public void EntityStatePersistsAcrossReconnect() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });

        var handler = new ServerPacketHandler(server);
        var c1 = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(c1);
        handler.Handle(c1, new LoginStartC2S("Persist", Guid.Empty));
        server.TryGetPlayer(c1.Id, out var h1);
        ref var t = ref h1.World.Ecs.Get<TransformEntityComponent>(h1.Entity);
        t.X = 40.5; t.Y = 70.0; t.Z = -12.5; t.Yaw = 90f; t.Pitch = 30f;
        h1.World.Ecs.Get<InventoryEntityComponent>(h1.Entity).Main(5) = new ItemStack(BlockRegistry.Cobblestone, 7);

        server.RemovePlayer(c1.Id);
        Assert.True(server.PlayerCount == 0, "disconnected");

        // Reconnect with the SAME name → same offline UUID → restored state.
        var c2 = new CaptureNetClient(2, protocol) { State = ConnectionState.Login };
        capture.Register(c2);
        handler.Handle(c2, new LoginStartC2S("Persist", Guid.Empty));
        server.TryGetPlayer(c2.Id, out var h2);
        var t2 = h2.World.Ecs.Get<TransformEntityComponent>(h2.Entity);
        var inv2 = h2.World.Ecs.Get<InventoryEntityComponent>(h2.Entity);
        Assert.True(t2.X == 40.5 && t2.Z == -12.5 && t2.Yaw == 90f && t2.Pitch == 30f,
            "position + rotation restored");
        Assert.True(inv2.Main(5).Type == BlockRegistry.Cobblestone && inv2.Main(5).Count == 7,
            "inventory restored");
    }

    // ── Persistence: the disk (RocksDB) serialization codec round-trips ─────────────
    [Fact]
    public void PlayerStateCodecRoundTrips() {
        var inv = new InventoryEntityComponent { SelectedSlot = 3 };
        inv.Main(0) = new ItemStack(BlockRegistry.Stone, 64);
        inv.Main(7) = Types.FromVanillaItem(194); // red wool, carrying its Color state
        var state = new PlayerState(new TransformEntityComponent(1.5, 70.0, -3.25, 45f, 12f), new HealthEntityComponent(15f, 20f), inv);

        var restored = PlayerStateCodec.Deserialize(PlayerStateCodec.Serialize(state));

        Assert.True(restored.Transform.X == 1.5 && restored.Transform.Yaw == 45f && restored.Transform.Pitch == 12f,
            "transform round-trips");
        Assert.True(restored.Health.Current == 15f && restored.Health.Max == 20f, "health round-trips");
        Assert.True(restored.Inventory.SelectedSlot == 3, "selected slot round-trips");
        Assert.True(restored.Inventory.Main(0).Type == BlockRegistry.Stone && restored.Inventory.Main(0).Count == 64,
            "plain stack round-trips");
        Assert.True(restored.Inventory.Main(7).Type == BlockRegistry.Wool
            && restored.Inventory.Main(7).State?.Get(State.Color) == 14,
            "stack with carried state round-trips");
    }

    // ── Persistence: world chunks (blocks, states, chest contents) survive a save/reload ──
    [Fact]
    public void WorldChunksPersistThroughStore() {
        var store = new InMemoryWorldStore();
        var pos = new Vector3i(2, 6, 2);
        var chestPos = new Vector3i(3, 6, 3);

        var w1 = new World("save", new FlatChunkGenerator(), store);
        w1.SetBlock(pos, BlockRegistry.Wool);
        w1.SetBlockState(pos, new BlockState(BlockRegistry.Wool).Set(State.Color, "red"));
        w1.SetBlock(chestPos, BlockRegistry.Chest);
        var chest = new BlockEntity(chestPos, BlockRegistry.Chest);
        var contents = new InventoryComponent(27);
        contents[0] = new ItemStack(BlockRegistry.Stone, 5);
        chest.Add(contents);
        w1.SetBlockEntity(chest);

        Assert.True(w1.Save() >= 1, "a modified chunk was saved");

        // A fresh world over the same store loads the chunk instead of regenerating it.
        var w2 = new World("save", new FlatChunkGenerator(), store);
        Assert.True(w2.GetBlock(pos) == BlockRegistry.Wool, "block persisted");
        Assert.True(w2.GetBlockState(pos)?.Get(State.Color) == 14, "block state (wool colour) persisted");
        Assert.True(w2.GetBlock(new Vector3i(0, 0, 0)) == BlockRegistry.Bedrock, "generated terrain persisted too");
        var be = w2.GetBlockEntity(chestPos);
        Assert.True(be is { } && be.Type == BlockRegistry.Chest
            && be.Get<InventoryComponent>()[0].Type == BlockRegistry.Stone && be.Get<InventoryComponent>()[0].Count == 5,
            "chest block entity + contents persisted");
    }

    // ── Persistence: a chunk only saves once it has been modified ──────────────────
    [Fact]
    public void OnlyModifiedChunksAreDirty() {
        var store = new InMemoryWorldStore();
        var world = new World("dirtytest", new FlatChunkGenerator(), store);
        world.GetBlock(new Vector3i(0, 0, 0)); // generate a chunk, no gameplay change
        Assert.True(world.Save() == 0, "a freshly generated chunk is not dirty");
        world.SetBlock(new Vector3i(1, 6, 1), BlockRegistry.Stone);
        Assert.True(world.Save() == 1, "a gameplay edit marks the chunk dirty");
        Assert.True(world.Save() == 0, "saving clears the dirty flag");
    }

    // ── Chunk streaming: the view follows the player across chunk boundaries ────────
    [Fact]
    public void ChunkStreamingFollowsPlayer() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "t", MaxPlayers = 20, TicksPerSecond = 20,
        });
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        handler.Handle(client, new LoginStartC2S("Walker", Guid.Empty));
        Assert.True(client.Sent.OfType<ChunkDataS2C>().Any(), "initial view streamed on join");
        var joinTeleport = client.Sent.OfType<SynchronizePlayerPositionS2C>().First();

        // Before confirming the join teleport, the client's position is stale → ignored (no stream).
        client.Sent.Clear();
        handler.Handle(client, new SetPlayerPositionC2S(40.0, FlatChunkGenerator.SurfaceY, 0.5, true));
        server.FlushSystems(); // streaming is now a per-tick system, not synchronous on the move packet
        Assert.True(!client.Sent.OfType<ChunkDataS2C>().Any(), "position ignored while teleport unconfirmed");

        // Confirm the teleport; now a move into a new column streams (spawn chunk 0,0; x=40 → chunk 2).
        handler.Handle(client, new ConfirmTeleportationC2S(joinTeleport.TeleportId));
        client.Sent.Clear();
        handler.Handle(client, new SetPlayerPositionC2S(40.0, FlatChunkGenerator.SurfaceY, 0.5, true));
        server.FlushSystems();
        Assert.True(client.Sent.OfType<SetCenterChunkS2C>().Any(c => c.ChunkX == 2 && c.ChunkZ == 0),
            "recenters on the new chunk");
        Assert.True(client.Sent.OfType<ChunkDataS2C>().Any(), "streams newly-visible columns");

        // Moving within the same column streams nothing new.
        client.Sent.Clear();
        handler.Handle(client, new SetPlayerPositionC2S(41.0, FlatChunkGenerator.SurfaceY, 1.0, true));
        server.FlushSystems();
        Assert.True(!client.Sent.OfType<ChunkDataS2C>().Any(), "no chunks while staying in a column");
    }

    // ── EventBus: polymorphic dispatch (heavy-context + generic events together) ────
    [Fact]
    public void EventBusDispatchesToBaseTypesAndInterfaces() {
        var bus = new EventBus();
        var fired = new List<string>();
        bus.Subscribe<DamageEvent>(_ => fired.Add("any-damage"));        // generic
        bus.Subscribe<ZombieDamage>(e => fired.Add($"zombie:{e.Amount}")); // specific, full context
        bus.Subscribe<IAudited>(_ => fired.Add("audit"));                // cross-cutting interface

        bus.Publish(new ZombieDamage(5)); // IS-A DamageEvent and IAudited

        Assert.Contains("any-damage", fired);
        Assert.Contains("zombie:5", fired);
        Assert.Contains("audit", fired);

        // Publishing the base must NOT reach the derived (specific) handler.
        fired.Clear();
        bus.Publish(new DamageEvent());
        Assert.Contains("any-damage", fired);
        Assert.DoesNotContain(fired, f => f.StartsWith("zombie"));
    }

    // ── EventBus: deferred publish only fires when the queue is drained ─────────────
    [Fact]
    public void DeferredEventsProcessOnlyOnDrain() {
        var bus = new EventBus();
        int count = 0;
        bus.Subscribe<DamageEvent>(_ => count++);

        bus.PublishDeferred(new DamageEvent());
        Assert.Equal(0, count); // queued, not yet run

        bus.DrainDeferred();
        Assert.Equal(1, count); // ran on drain (the "tick thread")

        bus.DrainDeferred();
        Assert.Equal(1, count); // nothing left to run
    }

    // ── Async persistence: write-behind reads-after-write and flushes on dispose ────
    [Fact]
    public void AsyncPlayerStoreReadsAfterWriteThenFlushes() {
        var inner = new InMemoryPlayerStore();
        var uuid = Guid.NewGuid();
        var state = new PlayerState(new TransformEntityComponent(1.0, 2.0, 3.0), new HealthEntityComponent(20f, 20f), new InventoryEntityComponent());

        using (var store = new AsyncPlayerStore(inner)) {
            store.Save(uuid, state); // queued, not necessarily flushed yet
            Assert.True(store.TryLoad(uuid, out var read) && read.Transform.X == 1.0,
                "read-after-write sees the queued value before flush");
        } // Dispose drains the queue to the inner store

        Assert.True(inner.TryLoad(uuid, out var flushed) && flushed.Transform.X == 1.0,
            "queued write was flushed to the inner store on dispose");
    }

    // ── Chunk eviction: drop out-of-range chunks, saving dirty ones first ──────────
    [Fact]
    public void ChunkEvictionDropsAndSavesOutOfRangeChunks() {
        var store = new InMemoryWorldStore();
        var world = new World("evict", new FlatChunkGenerator(), store);
        world.SetBlock(new Vector3i(1, 6, 1), BlockRegistry.Stone); // chunk (0,0,0) — dirty
        world.GetBlock(new Vector3i(1600, 4, 0));                   // chunk (100,0,0) — generated, clean
        int before = world.LoadedChunkCount;
        Assert.True(before >= 2, "two columns loaded");

        // Keep only column (0,0) within radius 1 — the far column is dropped.
        int evicted = world.EvictChunks(new List<(long, long)> { (0, 0) }, keepRadius: 1);
        Assert.True(evicted >= 1 && world.LoadedChunkCount < before, "far chunk evicted");
        Assert.True(world.GetChunk(new Vector3i(0, 0, 0)) is not null, "kept chunk still loaded");

        // Evict everything (no centres) — the dirty near chunk must be saved before it goes.
        world.EvictChunks(new List<(long, long)>(), keepRadius: 1);
        Assert.True(store.TryLoadChunk("evict", new Vector3i(0, 0, 0), out _),
            "dirty chunk was saved on eviction");
    }

    // ── Chunk eviction: never drops a dirty chunk when there's no store to save it ──
    [Fact]
    public void ChunkEvictionKeepsDirtyChunkWithoutStore() {
        var world = new World("nostore", new FlatChunkGenerator()); // no store
        world.SetBlock(new Vector3i(1, 6, 1), BlockRegistry.Stone);  // dirty chunk (0,0,0)
        int evicted = world.EvictChunks(new List<(long, long)>(), keepRadius: 1); // try to evict all
        Assert.True(evicted == 0 && world.LoadedChunkCount >= 1, "dirty chunk kept (no store to save to)");
    }

    // ── TypeMapper abstraction: per-protocol mapper, and codecs map via the stream ─────
    [Fact]
    public void ProtocolExposesMapperAndCodecsUseIt() {
        var protocol = new ProtocolJE763();
        // The mapper is now exposed per-protocol (was a static class welded to JE763).
        Assert.True(protocol.Types.StateId(BlockRegistry.Stone) == 1, "protocol exposes its type mapper");

        // A container packet carries domain ItemStacks; the codec maps them via the mapper that
        // EncodePayload sets on the stream — this would NullReference in SlotWire if the seam broke.
        var content = new SetContainerContentS2C(0, 0,
            new[] { new ItemStack(BlockRegistry.Stone, 5), protocol.Types.FromVanillaItem(194) /* red wool */ },
            default);
        var bytes = protocol.EncodePayload(content);
        Assert.True(bytes.Length > 0, "SetContainerContent encoded via the protocol's mapper");
    }

    // ── Broadcast cache: a message is encoded once per protocol version ────────────
    [Fact]
    public void BroadcastPacketEncodesOncePerVersion() {
        var protocol = new ProtocolJE763();
        var packet = new CachedPacket(new BlockUpdateS2C(new Vector3i(1, 2, 3), BlockRegistry.Stone));
        var first = packet.Framed(protocol);
        var second = packet.Framed(protocol);
        Assert.Same(first, second); // 2nd call hits the per-version cache — no re-encode
        Assert.True(first.Length > 0, "produced framed wire bytes");
    }

    // ── Legacy (JE61 / 1.5.2) framing: detection + non-VarInt wire format ──────────
    [Fact]
    public void LegacyProtocolDetectionAndFraming() {
        var registry = new ProtocolRegistry(new ProtocolJE763(), new ProtocolJE61());

        // First-byte detection: legacy markers route to JE61; a normal modern frame length stays modern.
        Assert.Equal(61, registry.Detect(0xFE).Version);  // legacy server-list ping
        Assert.Equal(61, registry.Detect(0x02).Version);  // legacy handshake
        Assert.Equal(763, registry.Detect(0x10).Version); // modern frame length

        var je61 = registry.Detect(0xFE);

        // Serverbound ping [0xFE][0x01] decodes with NO length prefix, straight off the live stream.
        using var ping = new MinecraftStream(new MemoryStream(new byte[] { 0xFE, 0x01 }, writable: false));
        Assert.Equal(new LegacyServerListPingC2S(1),
            je61.ReadMessage(ping, ConnectionState.Handshaking, PacketDirection.Serverbound));

        // Clientbound kick/ping-response: [0xFF][short charcount][UTF-16BE], no length prefix; round-trips.
        var kick = new LegacyKickS2C("§1\0test");
        byte[] framed = je61.Frame(kick);
        Assert.Equal((byte)0xFF, framed[0]);
        using var back = new MinecraftStream(new MemoryStream(framed, writable: false));
        Assert.Equal(kick, je61.ReadMessage(back, ConnectionState.Handshaking, PacketDirection.Clientbound));

        // An unknown legacy id can't be skipped (no length) → bail rather than desync.
        using var bad = new MinecraftStream(new MemoryStream(new byte[] { 0x99 }, writable: false));
        Assert.Throws<FormatException>(() =>
            je61.ReadMessage(bad, ConnectionState.Handshaking, PacketDirection.Serverbound));
    }

    [Fact]
    public void PeekByteIsReServed() {
        // A peeked byte is returned again by the next read, so a sniffed connection decodes identically.
        using var s = new MinecraftStream(new MemoryStream(new byte[] { 0x2A, 0x07 }, writable: false));
        Assert.Equal((byte)0x2A, s.PeekUByte());
        Assert.Equal((byte)0x2A, s.ReadUByte());
        Assert.Equal((byte)0x07, s.ReadUByte());
    }

    [Fact]
    public void LegacyString16RoundTrips() {
        using var ms = new MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true);
        w.WriteString16("§1\0hi");
        ms.Position = 0;
        Assert.Equal("§1\0hi", new MinecraftStream(ms).ReadString16());
    }

    // ── Legacy login: AES/CFB8 transport + login codecs ───────────────────────────
    [Fact]
    public void LegacyAesCfb8RoundTrips() {
        var key = new byte[16];
        for (int i = 0; i < 16; i++) key[i] = (byte)(i * 7 + 1);
        var plain = System.Text.Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog 0123456789!");

        var cipherMs = new MemoryStream();
        new AesCfb8Stream(cipherMs, key).Write(plain, 0, plain.Length); // encrypt → cipherMs
        byte[] cipher = cipherMs.ToArray();
        Assert.Equal(plain.Length, cipher.Length);  // CFB8 is a stream cipher (no padding)
        Assert.NotEqual(plain, cipher);              // actually encrypted

        var dec = new AesCfb8Stream(new MemoryStream(cipher), key);
        var outBuf = new byte[plain.Length];
        for (int read = 0; read < plain.Length;) {
            int r = dec.Read(outBuf, read, plain.Length - read);
            if (r <= 0) break;
            read += r;
        }
        Assert.Equal(plain, outBuf); // decrypt round-trips (key = IV)
    }

    [Fact]
    public void LegacyLoginCodecsRoundTrip() {
        var je61 = new ProtocolJE61();

        // Handshake 0x02 decode (single-byte id, fields straight off the stream).
        var hsMs = new MemoryStream();
        var w = new MinecraftStream(hsMs, leaveOpen: true);
        w.WriteUByte(0x02); w.WriteUByte(61); w.WriteString16("Steve"); w.WriteString16("localhost"); w.WriteInt(25565);
        hsMs.Position = 0;
        var hs = Assert.IsType<LegacyHandshakeC2S>(
            je61.ReadMessage(new MinecraftStream(hsMs), ConnectionState.Handshaking, PacketDirection.Serverbound));
        Assert.Equal((byte)61, hs.ProtocolVersion);
        Assert.Equal("Steve", hs.Username);
        Assert.Equal(25565, hs.Port);

        // Clientbound framing carries the right single-byte ids, no length prefix.
        Assert.Equal((byte)0x01, je61.Frame(new LegacyLoginRequestS2C(7, "flat", 1, 0, 1, 20))[0]);
        Assert.Equal((byte)0xFD, je61.Frame(new LegacyEncryptionRequestS2C("-", je61.PublicKeyDer, new byte[] { 1, 2, 3, 4 }))[0]);

        // The server's RSA round-trips a PKCS#1 blob (what the 0xFC decrypt path relies on).
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(je61.PublicKeyDer, out _);
        var secret = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 1, 2, 3, 4, 5, 6 };
        var encrypted = rsa.Encrypt(secret, System.Security.Cryptography.RSAEncryptionPadding.Pkcs1);
        Assert.Equal(secret, je61.DecryptRsa(encrypted));
    }

    [Fact]
    public void LegacyChunkSerializerBuildsFlatColumn() {
        var world = new World("test", new FlatChunkGenerator());
        var je61 = new ProtocolJE61();
        var chunk = LegacyChunkSerializer.Build(je61.Types, world, 0, 0);

        Assert.True((chunk.PrimaryBitmap & 1) != 0, "surface section present");
        Assert.Equal(0, chunk.AddBitmap);
        Assert.True(chunk.GroundUpContinuous);

        // Decompress; payload = present-section count × (blocks 4096 + 3 nibble arrays 2048) + biome 256.
        using var inflated = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(
                   new MemoryStream(chunk.CompressedData), System.IO.Compression.CompressionMode.Decompress))
            z.CopyTo(inflated);
        int sections = System.Numerics.BitOperations.PopCount((uint)chunk.PrimaryBitmap);
        Assert.Equal(sections * (4096 + 2048 * 3) + 256, (int)inflated.Length);

        Assert.Equal((byte)0x33, je61.Frame(chunk)[0]); // single-byte id, no length prefix
    }

    // ── Legacy chat (0x03): serverbound → generic; clientbound component → §-coded string ────
    [Fact]
    public void LegacyChatRoundTrips() {
        var je61 = new ProtocolJE61();

        // Serverbound 0x03 Chat → generic ChatMessageC2S, raw text (keeps a leading '/').
        var ms = new MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true);
        w.WriteUByte(0x03); w.WriteString16("/help");
        ms.Position = 0;
        var msg = Assert.IsType<ChatMessageC2S>(
            je61.ReadMessage(new MinecraftStream(ms), ConnectionState.Handshaking, PacketDirection.Serverbound));
        Assert.Equal("/help", msg.Message);

        // Clientbound SystemChatMessageS2C → 0x03 with a §-coded string (colour + formatting).
        var styled = new TextComponent("hi") { Color = "red", Bold = true };
        var framed = je61.Frame(new SystemChatMessageS2C(styled, Overlay: false));
        Assert.Equal((byte)0x03, framed[0]);
        Assert.Equal("§c§lhi", new MinecraftStream(new MemoryStream(framed[1..], writable: false)).ReadString16());

        // Nested Extra is concatenated; a modern hex colour quantizes to the nearest § colour.
        var compound = new TextComponent("a") { Color = "#ff5555" };       // ≈ red → §c
        compound.AddExtra(new TextComponent("b") { Color = "green" });      // → §a
        var cf = je61.Frame(new SystemChatMessageS2C(compound, Overlay: false));
        Assert.Equal("§ca§ab", new MinecraftStream(new MemoryStream(cf[1..], writable: false)).ReadString16());
    }

    // ── Chat JSON: a nested Extra component keeps its subclass fields (regression) ────
    [Fact]
    public void ChatComponentSerializesNestedExtraFields() {
        // The crash: Extra is List<ChatComponent>, so STJ used to serialize each element against the base
        // type and drop TextComponent.Text — the styled child collapsed to {"color":"dark_purple"}, which
        // the client rejected ("Don't know how to turn {...} into a Component"). The runtime-type dispatch
        // must now emit the child's text.
        var message = ChatComponent.Text("<")
            .AddExtra(ChatComponent.Text("Server").SetColor(TextColor.DarkPurple), ChatComponent.Text("> hi"));
        var json = message.ToString();
        Assert.Contains("\"text\":\"Server\"", json);
        Assert.Contains("\"color\":\"dark_purple\"", json);
        Assert.DoesNotContain("{\"color\":\"dark_purple\"}", json); // never a bare style-only child

        // …and it round-trips back to the same tree, with subclass types preserved.
        var back = ChatComponent.FromJson(json);
        var root = Assert.IsType<TextComponent>(back);
        Assert.Equal("<", root.Text);
        Assert.NotNull(root.Extra);
        var server = Assert.IsType<TextComponent>(root.Extra![0]);
        Assert.Equal("Server", server.Text);
        Assert.Equal("dark_purple", server.Color);
    }

    // ── Block drops: keep item-identity state (colour), reset placement state (facing) ────
    [Fact]
    public void DropStateKeepsColourResetsFacing() {
        // A facing chest's drop resets facing to default → all-default → carries no state on the drop.
        var chest = new BlockState(BlockRegistry.Chest).Set(State.Facing, "east");
        var chestDrop = chest.ForDrop();
        Assert.Equal(0, chestDrop.Get(State.Facing));
        Assert.True(chestDrop.Matches(new BlockState(BlockRegistry.Chest)), "facing-only state ⇒ stateless drop");

        // Red wool keeps its colour, so the drop carries it (a non-default, item-identity state).
        var wool = new BlockState(BlockRegistry.Wool).Set(State.Color, "red");
        var woolDrop = wool.ForDrop();
        Assert.Equal(State.Color.IndexOf("red"), woolDrop.Get(State.Color));
        Assert.False(woolDrop.Matches(new BlockState(BlockRegistry.Wool)), "colour kept ⇒ stateful drop");
    }

    // ── Physics: a generic entity falls under gravity and rests on terrain ─────────
    [Fact]
    public void DroppedItemFallsAndRestsOnGround() {
        var world = new World("physics", new FlatChunkGenerator());
        // Spawn well above the flat surface (grass occupies y=4, so its top face is y=5).
        var entity = world.SpawnDroppedItem(new Vector3i(0, 12, 0), new ItemStack(BlockRegistry.Stone, 1));
        for (int i = 0; i < 60; i++) world.Tick();

        var t = world.Ecs.Get<TransformEntityComponent>(entity);
        var v = world.Ecs.Get<VelocityEntityComponent>(entity);
        Assert.True(System.Math.Abs(t.Y - 5.0) < 1e-3, $"item rests on the surface (Y={t.Y}, expected 5)");
        Assert.True(System.Math.Abs(v.Y) < 1e-6, "vertical velocity settled to rest");
    }

    // ── Physics: terrain collision stops horizontal motion at a wall ───────────────
    [Fact]
    public void PhysicsStopsAtWalls() {
        var world = new World("walls", new FlatChunkGenerator());
        world.SetBlock(new Vector3i(1, 5, 0), BlockRegistry.Stone); // a wall east of the spawn

        var entity = world.SpawnDroppedItem(new Vector3i(0, 5, 0), new ItemStack(BlockRegistry.Stone, 1));
        world.Ecs.Get<VelocityEntityComponent>(entity) = new VelocityEntityComponent(0.5, 0, 0); // override the random scatter: shove it due east
        for (int i = 0; i < 20; i++) world.Tick();

        var t = world.Ecs.Get<TransformEntityComponent>(entity);
        // The wall's west face is x=1, so the box's right edge can't push past it (read the actual
        // collider half-width so the test tracks the item's real size, not a hardcoded one).
        var hw = world.Ecs.Get<ColliderEntityComponent>(entity).HalfWidth;
        Assert.True(t.X + hw <= 1.0 + 1e-3, $"item stopped at the wall (X={t.X}, hw={hw})");
    }

    // ── Collision: a spawned player's CollisionFeedback is reliably constructed (no null list) ──
    [Fact]
    public void PlayerCollisionFeedbackIsInitialized() {
        // The production NRE came from a parameterless struct ctor (bypassed under the Release JIT)
        // leaving Touching null. Player.Spawn now sets it via an object initializer, so it's always
        // present — and the collision pass runs without dereferencing null.
        var world = new World("collide", new FlatChunkGenerator());
        var player = world.SpawnPlayer(1, "P", Guid.NewGuid(), 1);

        Assert.NotNull(world.Ecs.Get<CollisionFeedbackEntityComponent>(player).Touching);
        Assert.Null(Record.Exception(() => world.Tick()));
    }

    // ── Spatial index: chunk-bucketed lookups + incremental move/remove maintenance ────────
    [Fact]
    public void SpatialIndexBucketsAndQueries() {
        var world = new World("idx", new FlatChunkGenerator());
        var idx = world.Entities;

        var a = world.Ecs.Create(new TransformEntityComponent(2, 5, 2));    // chunk-cube (0,0,0)
        var b = world.Ecs.Create(new TransformEntityComponent(40, 5, 2));   // chunk-cube (2,0,0), 38 blocks away
        idx.Add(a, 2, 5, 2);
        idx.Add(b, 40, 5, 2);

        // Ranged lookup: only the near entity is within radius.
        var near = new List<ArchEntity>();
        idx.Near(2, 5, 2, 3.0, near);
        Assert.Contains(a, near);
        Assert.DoesNotContain(b, near);

        // Per-chunk lookup buckets them separately.
        Assert.Contains(a, idx.InChunk(new Vector3i(0, 0, 0)));
        Assert.Contains(b, idx.InChunk(new Vector3i(2, 0, 0)));

        // Move 'a' across chunk boundaries → it re-buckets (the EntityMoved-driven path).
        idx.Update(a, 200, 5, 2); // chunk-cube (12,0,0)
        Assert.DoesNotContain(a, idx.InChunk(new Vector3i(0, 0, 0)));
        Assert.Contains(a, idx.InChunk(new Vector3i(12, 0, 0)));

        // Remove drops it entirely.
        idx.Remove(b);
        Assert.DoesNotContain(b, idx.InChunk(new Vector3i(2, 0, 0)));
    }

    // ── Spatial index: spawn/despawn keep it consistent through the World lifecycle ─────────
    [Fact]
    public void SpawnAndDespawnMaintainSpatialIndex() {
        var world = new World("idxspawn", new FlatChunkGenerator());
        var cell = new Vector3i(0, 0, 0); // block (5,6,5) → chunk-cube (0,0,0)

        var item = world.SpawnDroppedItem(new Vector3i(5, 6, 5), new ItemStack(BlockRegistry.Stone, 1));
        Assert.Contains(item, world.Entities.InChunk(cell));

        world.DestroyEntity(item);
        Assert.DoesNotContain(item, world.Entities.InChunk(cell));
    }

    // ── Events: a non-player entity raises the generic EntityMoved when physics moves it ────
    [Fact]
    public void NonPlayerEntityRaisesEntityMoved() {
        var events = new EventBus();
        var moved = new List<ArchEntity>();
        events.Subscribe<EntityMoved>(e => moved.Add(e.Entity));

        var world = new World("emove", new FlatChunkGenerator()) { Events = events };
        // Spawn above the surface so gravity actually shifts it; pin a known scatter so it moves.
        var item = world.SpawnDroppedItem(new Vector3i(0, 12, 0), new ItemStack(BlockRegistry.Stone, 1));

        for (int i = 0; i < 5; i++) { world.Tick(); events.DrainDeferred(); }
        Assert.Contains(item, moved); // the falling item published EntityMoved (deferred → drained)

        // Once it settles, the move events stop (MoveEpsilon rest cut-off).
        for (int i = 0; i < 60; i++) { world.Tick(); events.DrainDeferred(); }
        moved.Clear();
        for (int i = 0; i < 5; i++) { world.Tick(); events.DrainDeferred(); }
        Assert.Empty(moved); // at rest: no more move events
    }

    // ── Ticking: a tickable block entity is ticked through World.Tick → Chunk.Tick ─────
    [Fact]
    public void TickableBlockEntityIsTickedByItsChunk() {
        var world = new World("betick", new FlatChunkGenerator());
        var pos = new Vector3i(3, 6, 3);
        var counter = new CountingBlockEntity(pos);
        world.SetBlockEntity(counter);

        world.Tick();
        world.Tick();
        Assert.Equal(2, counter.Ticks);

        // Removing it stops the ticks (and a non-ticking block entity never counted to begin with).
        world.RemoveBlockEntity(pos);
        world.Tick();
        Assert.Equal(2, counter.Ticks);
    }

    sealed class CountingBlockEntity : BlockEntity, ITickable {
        public int Ticks;
        public CountingBlockEntity(Vector3i pos) : base(pos, BlockRegistry.Chest) { }
        public void Tick() => Ticks++;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    static int DropCount(World world) =>
        world.Ecs.CountEntities(in new QueryDescription().WithAll<PickupEntityComponent>());

    static bool RoundTrip(Protocol protocol, ConnectionState state, IMessage message) {
        // EncodePayload writes [VarInt id][body] using the type's registered codec.
        var payload = protocol.EncodePayload(message);
        using var ms = new MinecraftStream(new MemoryStream(payload, writable: false));
        int id = ms.ReadVarInt();
        var codec = protocol.CodecFor(state, PacketDirection.Serverbound, id);
        return codec is not null && codec.Decode(ms).Equals(message);
    }

    // ── Command parse cache: a player's parses are invalidated when their .Requires inputs change ──
    [Fact]
    public void CommandParseCacheInvalidatesPerPlayer() {
        var server = new Server(new ServerContext {
            NetServer = new CaptureNetServer(new ProtocolJE763()),
            Worlds = new ConcurrentDictionary<string, World> { ["overworld"] = new World("overworld", new FlatChunkGenerator()) },
            MOTD = "t", MaxPlayers = 8, TicksPerSecond = 20,
        });
        var dispatcher = server.CommandDispatcher; // the dispatcher now needs its Server (cache scales with player count)
        bool allow = false; // stand-in for a permission / world-gate that .Requires depends on
        var replies = new List<string>();
        var sender = new CaptureSender(replies);
        var client = new CaptureNetClient(7, new ProtocolJE763());

        dispatcher.Register(l => l.Literal("secret").Requires(_ => allow)
            .Executes(c => { c.Source.Reply("ok"); return 1; }));

        // Denied: the literal is pruned, so it parses as unknown — and that parse is cached for this player.
        _ = dispatcher.ExecuteAsync(sender, "secret", client); // synchronous-bodied; completes before returning
        Assert.DoesNotContain("ok", replies);

        // Flipping the gate alone changes nothing: the cached (pruned) parse is re-executed verbatim.
        allow = true;
        _ = dispatcher.ExecuteAsync(sender, "secret", client); // synchronous-bodied; completes before returning
        Assert.DoesNotContain("ok", replies);

        // Invalidating the player re-keys the cache, so the next run re-parses against the new gate state.
        dispatcher.Invalidate(client.Id);
        _ = dispatcher.ExecuteAsync(sender, "secret", client); // synchronous-bodied; completes before returning
        Assert.Contains("ok", replies);
    }

    [Fact]
    public void WorldCommandSuggestsExistingWorldsFromServer() {
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
            ["nether"] = new World("nether", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = new CaptureNetServer(new ProtocolJE763()), Worlds = worlds,
            MOTD = "t", MaxPlayers = 8, TicksPerSecond = 20,
        });
        server.CommandDispatcher.RegisterWorld();
        // A player source (client != null) so .Requires(IsPlayer) passes — same as HandleSuggestions builds.
        var client = new CaptureNetClient(1, new ProtocolJE763());
        var source = new SenderContext(new CaptureSender(new()), server.CommandDispatcher, client);
        var brig = server.CommandDispatcher.Brigadier;

        var all = brig.GetCompletionSuggestions(brig.Parse("world ", source)).GetAwaiter().GetResult()
            .List.Select(s => s.Text).ToArray();
        Assert.Contains("overworld", all);
        Assert.Contains("nether", all);
    }

    // ── Regression: the real client sends the leading '/' in the suggestion request (SharpTester didn't) ──
    // The dispatcher must skip it for parsing yet keep the range in the client's coordinates, or ask_server
    // value suggestions (player/world) silently return nothing while client-side literals still work.
    [Fact]
    public void SuggestStripsLeadingSlashAndKeepsClientRange() {
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = new CaptureNetServer(new ProtocolJE763()), Worlds = worlds,
            MOTD = "t", MaxPlayers = 8, TicksPerSecond = 20,
        });
        server.CommandDispatcher.RegisterWorld();
        var client = new CaptureNetClient(1, new ProtocolJE763());
        var sender = new CaptureSender(new());

        // What the vanilla client actually sends for "/world " + Tab: the whole input, slash included.
        var (start, length, matches) = server.CommandDispatcher.Suggest(sender, "/world ", client);
        Assert.Contains("overworld", matches);             // values come back (the bug: they didn't)
        Assert.Equal("/world ".Length, start);             // range start is at the arg in the client's input (index 7)
        Assert.Equal(0, length);

        // And a partial with the slash: "/world over" → replaces "over" at index 7, length 4.
        var (pStart, pLength, pMatches) = server.CommandDispatcher.Suggest(sender, "/world over", client);
        Assert.Contains("overworld", pMatches);
        Assert.Equal(7, pStart);
        Assert.Equal(4, pLength);
    }

    // ── Declare Commands BYTES: the world/tp value args must reach the client flagged ask_server ──
    // (in-process suggestion tests use the server's dispatcher; this is the only check on the actual wire
    //  graph the client rebuilds its tree from — the gap behind "shows <arg> placeholder but no values".)
    [Fact]
    public void DeclareCommandsFlagsValueArgsAsAskServer() {
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = new CaptureNetServer(new ProtocolJE763()), Worlds = worlds,
            MOTD = "t", MaxPlayers = 8, TicksPerSecond = 20,
        });
        server.CommandDispatcher.RegisterWorld().RegisterTp();
        var client = new CaptureNetClient(1, new ProtocolJE763());
        var source = new SenderContext(new CaptureSender(new()), server.CommandDispatcher, client);

        using var ms = new System.IO.MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true) { Types = new TypeMapperJE763() };
        CommandTreeSerializer.Write(w, server.CommandDispatcher.Brigadier.GetRoot(), source);

        ms.Position = 0;
        var nodes = ParseDeclareCommands(new MinecraftStream(ms, leaveOpen: true));

        var worldArg = nodes.Single(n => n.Name == "name");
        Assert.True(worldArg.HasCustomSuggestions, "world <name> must be flagged has_custom_suggestions on the wire");
        Assert.Equal("minecraft:ask_server", worldArg.SuggestionType);
        var playerArg = nodes.Single(n => n.Name == "player");
        Assert.True(playerArg.HasCustomSuggestions, "tp <player> must be flagged has_custom_suggestions on the wire");
        Assert.Equal("minecraft:ask_server", playerArg.SuggestionType);
    }

    sealed record ParsedNode(int Type, string? Name, bool HasCustomSuggestions, string? SuggestionType);

    // Parses the 1.20.1 Commands node graph the way a client does, so the test sees exactly what was sent.
    static List<ParsedNode> ParseDeclareCommands(MinecraftStream s) {
        int count = s.ReadVarInt();
        var result = new List<ParsedNode>(count);
        for (int i = 0; i < count; i++) {
            byte flags = s.ReadUByte();
            int type = flags & 0x03;
            bool hasRedirect = (flags & 0x08) != 0;
            bool hasSuggestions = (flags & 0x10) != 0;
            int children = s.ReadVarInt();
            for (int c = 0; c < children; c++) s.ReadVarInt();
            if (hasRedirect) s.ReadVarInt();
            string? name = null, suggestionType = null;
            if (type == 1) {
                name = s.ReadString();
            } else if (type == 2) {
                name = s.ReadString();
                SkipParser(s);
                if (hasSuggestions) suggestionType = s.ReadString();
            }
            result.Add(new ParsedNode(type, name, hasSuggestions, suggestionType));
        }
        return result;
    }

    static void SkipParser(MinecraftStream s) {
        int parser = s.ReadVarInt();
        switch (parser) {
            case 0: break;                                 // brigadier:bool
            case 1: SkipBounds(s, () => s.ReadFloat()); break;
            case 2: SkipBounds(s, () => s.ReadDouble()); break;
            case 3: SkipBounds(s, () => s.ReadInt()); break;
            case 4: SkipBounds(s, () => s.ReadLong()); break;
            case 5: s.ReadVarInt(); break;                 // brigadier:string mode
            default: throw new InvalidOperationException($"unexpected parser id {parser}");
        }
    }

    static void SkipBounds(MinecraftStream s, Action readBound) {
        byte f = s.ReadUByte();
        if ((f & 0x01) != 0) readBound();
        if ((f & 0x02) != 0) readBound();
    }

    sealed class CaptureSender : ISender {
        readonly List<string> messages;
        public CaptureSender(List<string> messages) => this.messages = messages;
        public string Name => "test";
        public void ReceiveMessage(ChatComponent message) =>
            messages.Add(message is TextComponent t ? t.Text : message.ToString());
    }

    // ── In-memory transport doubles ─────────────────────────────────────────
    sealed class CaptureNetClient : NetClient {
        public readonly List<IMessage> Sent = new();
        public CaptureNetClient(ulong id, Protocol protocol) : base(id, protocol) { }
        public override void Send(IMessage message) => Sent.Add(message);
        public override void Send(CachedPacket packet) => Sent.Add(packet.Message);
        public override void Disconnect() { }
    }

    sealed class CaptureNetServer : INetServer {
        public readonly List<IMessage> Broadcasts = new();
        readonly List<NetClient> clients = new();
        public ProtocolRegistry Registry { get; }
        public Protocol Protocol => Registry.Default;
        public CaptureNetServer(Protocol protocol) => Registry = new ProtocolRegistry(protocol);
        public void Register(NetClient client) => clients.Add(client);
        public void Start() { }
        public void Stop() { }
        public bool TryGetClient(ulong id, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out NetClient client) {
            client = clients.FirstOrDefault(c => c.Id == id);
            return client is not null;
        }
        public void Send(ulong client, IMessage message) {
            foreach (var c in clients) if (c.Id == client) c.Send(message);
        }
        public void Broadcast(IMessage message, Func<NetClient, bool>? predicate = null) {
            Broadcasts.Add(message);
            foreach (var c in clients) if (predicate is null || predicate(c)) c.Send(message);
        }
    }
}

// Test-only event hierarchy for the polymorphic-dispatch test (a ZombieDamage IS-A DamageEvent
// and IS-A IAudited).
interface IAudited;
record DamageEvent : IAudited;
sealed record ZombieDamage(int Amount) : DamageEvent;
