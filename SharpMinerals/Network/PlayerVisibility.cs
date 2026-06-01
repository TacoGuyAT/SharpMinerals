using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Items;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network;

/// <summary>
/// Keeps every client's view of the other players in sync: profiles (tab list +
/// skins), entity spawns, movement, and removal. Sends are targeted per-client so a
/// player never receives its own spawn.
/// </summary>
public static class PlayerVisibility {
    const int CreativeMode = 1;

    /// <summary>Wires player visibility to the join/move/leave/inventory events on a bus.</summary>
    public static void Register(EventBus events) {
        events.Subscribe<PlayerJoined>(e => OnJoin(e.Context.Server, e.Context.Client));
        events.Subscribe<PlayerMoved>(e => OnMove(e.Context.Server, e.Context.Client));
        events.Subscribe<PlayerLeft>(e => OnLeave(e.Context.Server, e.Context.Player));
        events.Subscribe<PlayerInventoryChanged>(e => OnInventoryChanged(e.PlayerContext));
    }

    static PlayerListEntry Entry(in NetPlayerEntityComponent p) => new(p.Uuid, p.Name, CreativeMode, Listed: true, Latency: 0);

    /// <summary>The non-empty equipment a player renders — held item, off-hand, and the four armour pieces
    /// — as generic Set Equipment messages.</summary>
    public static IEnumerable<SetEquipmentS2C> Equipment(int entityId, InventoryEntityComponent inv) {
        if (!inv.Held.IsEmpty)    yield return new(entityId, EquipmentSlot.MainHand, inv.Held);
        if (!inv.Offhand.IsEmpty) yield return new(entityId, EquipmentSlot.OffHand, inv.Offhand);
        var feet = inv.Armor(ArmorSlot.Feet);   if (!feet.IsEmpty)  yield return new(entityId, EquipmentSlot.Boots, feet);
        var legs = inv.Armor(ArmorSlot.Legs);   if (!legs.IsEmpty)  yield return new(entityId, EquipmentSlot.Leggings, legs);
        var chest = inv.Armor(ArmorSlot.Chest); if (!chest.IsEmpty) yield return new(entityId, EquipmentSlot.Chestplate, chest);
        var head = inv.Armor(ArmorSlot.Head);   if (!head.IsEmpty)  yield return new(entityId, EquipmentSlot.Helmet, head);
    }

    // The off-hand slot was added in 1.9 (protocol 107). Older protocols (e.g. 1.5.2 = 61) have no
    // off-hand and can't encode one — their equipment codec would crash mid-encode if handed one.
    const int OffHandMinProtocol = 107;

    /// <summary>Whether a client's protocol can render an off-hand item. A legacy in-world client is still
    /// in the Play state, so we gate on protocol VERSION, not connection state. This is the single
    /// chokepoint that keeps off-hand equipment off the legacy wire (where its encoder has no off-hand slot
    /// and would throw mid-encode); every off-hand send must be guarded by it.</summary>
    public static bool CanSeeOffhand(NetClient c) => c.Protocol.Version >= OffHandMinProtocol;

    // Broadcasts one equipment slot of a player to every OTHER in-world client that may see it. Off-hand
    // goes only to protocols that have one (never the legacy wire, whose encoder would throw). The single
    // send point for equipment, so the off-hand gate can't be bypassed.
    static void BroadcastEquipment(Server server, ulong selfClientId, int entityId, EquipmentSlot slot, ItemStack item) =>
        server.NetServer.Broadcast(new SetEquipmentS2C(entityId, slot, item),
            c => c.Id != selfClientId && c.InWorld && (slot != EquipmentSlot.OffHand || CanSeeOffhand(c)));

    // A player's six equipment stacks indexed by EquipmentSlot ordinal — what others should see them
    // wearing/holding, derived from the live inventory. Compared against SyncedEquipment to find changes.
    static ItemStack[] EquipmentSnapshot(InventoryEntityComponent inv) => new[] {
        inv.Held,                      // 0 MainHand
        inv.Offhand,                   // 1 OffHand
        inv.Armor(ArmorSlot.Feet),     // 2 Boots
        inv.Armor(ArmorSlot.Legs),     // 3 Leggings
        inv.Armor(ArmorSlot.Chest),    // 4 Chestplate
        inv.Armor(ArmorSlot.Head),     // 5 Helmet
    };

    /// <summary>A player's inventory changed (pickup, container click, creative set, toss, or join):
    /// broadcast every equipment slot whose rendered item differs from what other clients were last shown,
    /// and record the new state in <see cref="EquipmentEntityComponent"/>. Count-only changes (placing from the held
    /// stack) are ignored — the held model looks the same.</summary>
    public static void OnInventoryChanged(PlayerContext context) {
        var ecs = context.World.Ecs;
        if (!ecs.IsAlive(context.Entity)) return;
        var inv = ecs.Get<InventoryEntityComponent>(context.Entity);
        var synced = ecs.Get<EquipmentEntityComponent>(context.Entity).LastSent;
        int eid = ecs.Get<NetPlayerEntityComponent>(context.Entity).EntityId;
        var current = EquipmentSnapshot(inv);
        for (int i = 0; i < current.Length; i++)
            if (!LooksSame(synced[i], current[i])) {
                BroadcastEquipment(context.Server, context.Client.Id, eid, (EquipmentSlot)i, current[i]);
                synced[i] = current[i];
            }
    }

    // Equipment renders the item model (and carried state, e.g. wool colour), not the count — so two
    // non-empty stacks of the same item+state look identical. Only type/state/empty transitions matter.
    static bool LooksSame(ItemStack a, ItemStack b) =>
        a.IsEmpty || b.IsEmpty ? a.IsEmpty && b.IsEmpty : a.StacksWith(b);

    /// <summary>A player just joined: introduce it to others and others to it.</summary>
    public static void OnJoin(Server server, NetClient client) {
        if (!server.TryGetPlayer(client.Id, out var self) || !self.World.Ecs.IsAlive(self.Entity))
            return;

        var selfInfo = self.World.Ecs.Get<NetPlayerEntityComponent>(self.Entity);
        var selfPos = self.World.Ecs.Get<TransformEntityComponent>(self.Entity);

        // Everyone (including the newcomer, for its own tab list) learns the new
        // profile. Existing clients already have the newcomer's profile before we
        // spawn its entity to them below.
        server.NetServer.Broadcast(new PlayerInfoUpdateS2C(new[] { Entry(selfInfo) }),
            c => c.State == ConnectionState.Play);

        var others = new List<(NetClient Client, NetPlayerEntityComponent Info, TransformEntityComponent Pos, InventoryEntityComponent Inv)>();
        foreach (var (clientId, context) in server.Players) {
            if (clientId == client.Id || !context.World.Ecs.IsAlive(context.Entity))
                continue;
            others.Add((context.Client, context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity),
                context.World.Ecs.Get<TransformEntityComponent>(context.Entity),
                context.World.Ecs.Get<InventoryEntityComponent>(context.Entity)));
        }

        if (others.Count > 0) {
            // The newcomer must learn the existing profiles BEFORE their entities are
            // spawned, or it can't render them.
            client.Send(new PlayerInfoUpdateS2C(others.Select(o => Entry(o.Info)).ToList()));
            foreach (var o in others) {
                client.Send(Spawn(o.Info, o.Pos));
                // A spawn renders a default standing entity; replay any active flags (sneaking, …)
                // and whatever they're holding/wearing.
                if (o.Info.Flags != EntityFlags.None)
                    client.Send(new EntityFlagsS2C(o.Info.EntityId, o.Info.Flags));
                foreach (var eq in Equipment(o.Info.EntityId, o.Inv))
                    if (eq.Slot != EquipmentSlot.OffHand || CanSeeOffhand(client)) client.Send(eq);
            }
        }

        // Spawn the newcomer's entity for each existing client.
        foreach (var o in others)
            o.Client.Send(Spawn(selfInfo, selfPos));

        // Now show the newcomer's equipment to everyone else and seed its SyncedEquipment — a restored
        // player may already hold an item / wear armour. Same path as any later inventory change.
        OnInventoryChanged(self);
    }

    /// <summary>A player moved: push its new transform to everyone else.</summary>
    public static void OnMove(Server server, NetClient client) {
        if (!server.TryGetPlayer(client.Id, out var self) || !self.World.Ecs.IsAlive(self.Entity))
            return;

        var info = self.World.Ecs.Get<NetPlayerEntityComponent>(self.Entity);
        var pos = self.World.Ecs.Get<TransformEntityComponent>(self.Entity);

        // In-world recipients include legacy clients (which are never in the Play state); each gets the
        // movement in its own format (modern Teleport Entity / legacy 0x22). Never to the mover itself.
        bool ToOthers(NetClient c) => c.InWorld && c.Id != client.Id;
        server.NetServer.Broadcast(new TeleportEntityS2C(info.EntityId, pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch, true), ToOthers);
        server.NetServer.Broadcast(new EntityHeadRotationS2C(info.EntityId, pos.Yaw), ToOthers);
    }

    /// <summary>A player left: despawn its entity and drop it from the tab list everywhere.</summary>
    public static void OnLeave(Server server, NetPlayerEntityComponent info) {
        server.NetServer.Broadcast(new RemoveEntitiesS2C(new[] { info.EntityId }), c => c.InWorld);
        server.NetServer.Broadcast(new PlayerInfoRemoveS2C(new[] { info.Uuid }), c => c.State == ConnectionState.Play);
    }

    static SpawnPlayerS2C Spawn(in NetPlayerEntityComponent info, in TransformEntityComponent pos) =>
        new(info.EntityId, info.Uuid, info.Name, pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch);
}
