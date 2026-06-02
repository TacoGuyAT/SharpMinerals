using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Items;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network;

/// <summary>
/// Keeps every client's view of the other players in sync: profiles, entity spawns, movement, removal.
/// Sends are targeted per-client so a player never receives its own spawn.
/// </summary>
public static class PlayerVisibility {
    const int CreativeMode = 1;

    public static void Register(EventBus events) {
        events.Subscribe<PlayerJoined>(e => OnJoin(e.Context.Server, e.Context.Client));
        events.Subscribe<PlayerMoved>(e => OnMove(e.Context.Server, e.Context.Client));
        events.Subscribe<PlayerLeft>(e => OnLeave(e.Context.Server, e.Context.Player));
        events.Subscribe<PlayerInventoryChanged>(e => OnInventoryChanged(e.PlayerContext));
    }

    static PlayerListEntry Entry(in NetPlayerEntityComponent p) => new(p.Uuid, p.Name, CreativeMode, Listed: true, Latency: 0);

    /// <summary>The non-empty equipment a player renders (held, off-hand, four armour pieces) as Set Equipment messages.</summary>
    public static IEnumerable<SetEquipmentS2C> Equipment(int entityId, InventoryEntityComponent inv) {
        if (!inv.Held.IsEmpty)    yield return new(entityId, EquipmentSlot.MainHand, inv.Held);
        if (!inv.Offhand.IsEmpty) yield return new(entityId, EquipmentSlot.OffHand, inv.Offhand);
        var feet = inv.Armor(ArmorSlot.Feet);   if (!feet.IsEmpty)  yield return new(entityId, EquipmentSlot.Boots, feet);
        var legs = inv.Armor(ArmorSlot.Legs);   if (!legs.IsEmpty)  yield return new(entityId, EquipmentSlot.Leggings, legs);
        var chest = inv.Armor(ArmorSlot.Chest); if (!chest.IsEmpty) yield return new(entityId, EquipmentSlot.Chestplate, chest);
        var head = inv.Armor(ArmorSlot.Head);   if (!head.IsEmpty)  yield return new(entityId, EquipmentSlot.Helmet, head);
    }

    // Off-hand slot added in 1.9 (protocol 107); older protocols can't encode one and would throw mid-encode.
    const int OffHandMinProtocol = 107;

    /// <summary>Whether a client's protocol can render an off-hand item. Gated on protocol version, not
    /// connection state (legacy in-world clients are still in Play); every off-hand send must be guarded by it.</summary>
    public static bool CanSeeOffhand(NetClient c) => c.Protocol.Version >= OffHandMinProtocol;

    // Single send point for equipment, so the off-hand gate can't be bypassed.
    static void BroadcastEquipment(Server server, ulong selfClientId, int entityId, EquipmentSlot slot, ItemStack item) =>
        server.NetServer.Broadcast(new SetEquipmentS2C(entityId, slot, item),
            c => c.Id != selfClientId && c.InWorld && (slot != EquipmentSlot.OffHand || CanSeeOffhand(c)));

    // A player's six equipment stacks indexed by EquipmentSlot ordinal; compared against SyncedEquipment to find changes.
    static ItemStack[] EquipmentSnapshot(InventoryEntityComponent inv) => new[] {
        inv.Held,                      // 0 MainHand
        inv.Offhand,                   // 1 OffHand
        inv.Armor(ArmorSlot.Feet),     // 2 Boots
        inv.Armor(ArmorSlot.Legs),     // 3 Leggings
        inv.Armor(ArmorSlot.Chest),    // 4 Chestplate
        inv.Armor(ArmorSlot.Head),     // 5 Helmet
    };

    /// <summary>A player's inventory changed: broadcast every equipment slot whose rendered item differs
    /// from what other clients were last shown, and record the new state. Count-only changes are ignored.</summary>
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

    // Equipment renders the item model + carried state, not the count; only type/state/empty transitions matter.
    static bool LooksSame(ItemStack a, ItemStack b) =>
        a.IsEmpty || b.IsEmpty ? a.IsEmpty && b.IsEmpty : a.StacksWith(b);

    public static void OnJoin(Server server, NetClient client) {
        if (!server.TryGetPlayer(client.Id, out var self) || !self.World.Ecs.IsAlive(self.Entity))
            return;

        var selfInfo = self.World.Ecs.Get<NetPlayerEntityComponent>(self.Entity);
        var selfPos = self.World.Ecs.Get<TransformEntityComponent>(self.Entity);

        // Everyone (incl. the newcomer, for its tab list) learns the new profile before its entity is spawned below.
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
            // The newcomer must learn existing profiles before their entities are spawned, or it can't render them.
            client.Send(new PlayerInfoUpdateS2C(others.Select(o => Entry(o.Info)).ToList()));
            foreach (var o in others) {
                client.Send(Spawn(o.Info, o.Pos));
                // A spawn renders a default standing entity; replay active flags and equipment.
                if (o.Info.Flags != EntityFlags.None)
                    client.Send(new EntityFlagsS2C(o.Info.EntityId, o.Info.Flags));
                foreach (var eq in Equipment(o.Info.EntityId, o.Inv))
                    if (eq.Slot != EquipmentSlot.OffHand || CanSeeOffhand(client)) client.Send(eq);
            }
        }

        foreach (var o in others)
            o.Client.Send(Spawn(selfInfo, selfPos));

        // Show the newcomer's equipment to everyone else and seed its SyncedEquipment (a restored player
        // may already hold/wear items). Same path as any later inventory change.
        OnInventoryChanged(self);
    }

    public static void OnMove(Server server, NetClient client) {
        if (!server.TryGetPlayer(client.Id, out var self) || !self.World.Ecs.IsAlive(self.Entity))
            return;

        var info = self.World.Ecs.Get<NetPlayerEntityComponent>(self.Entity);
        var pos = self.World.Ecs.Get<TransformEntityComponent>(self.Entity);

        // In-world recipients include legacy clients; each gets the movement in its own format. Never the mover itself.
        bool ToOthers(NetClient c) => c.InWorld && c.Id != client.Id;
        server.NetServer.Broadcast(new TeleportEntityS2C(info.EntityId, pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch, true), ToOthers);
        server.NetServer.Broadcast(new EntityHeadRotationS2C(info.EntityId, pos.Yaw), ToOthers);
    }

    public static void OnLeave(Server server, NetPlayerEntityComponent info) {
        server.NetServer.Broadcast(new RemoveEntitiesS2C(new[] { info.EntityId }), c => c.InWorld);
        server.NetServer.Broadcast(new PlayerInfoRemoveS2C(new[] { info.Uuid }), c => c.State == ConnectionState.Play);
    }

    static SpawnPlayerS2C Spawn(in NetPlayerEntityComponent info, in TransformEntityComponent pos) =>
        new(info.EntityId, info.Uuid, info.Name, pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch);
}
