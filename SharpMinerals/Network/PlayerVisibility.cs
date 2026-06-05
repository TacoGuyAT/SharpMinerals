using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Items;
using SharpMinerals.Network.Messages;
using ArchWorld = Arch.Core.World;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Network;

/// <summary>
/// Keeps every client's TAB LIST (player profiles) in sync on join/leave, and builds the per-player spawn for the
/// entity tracker. The player ENTITY spawn/despawn (and distance culling) is owned by
/// <c>Level.Systems.EntityTrackerSystem</c>; this only handles the range-independent profile list and equipment.
/// </summary>
public static class PlayerVisibility {
    const int CreativeMode = 1;

    public static void Register(EventBus events) {
        events.Subscribe<PlayerJoined>(e => OnJoin(e.Context.Server, e.Context.Client));
        events.Subscribe<PlayerLeft>(e => OnLeave(e.Context.Server, e.Context.Player));
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

        // Everyone (incl. the newcomer, for its tab list) learns the new profile; the entity tracker spawns the
        // actual player entities per-view on the next flush.
        server.NetServer.Broadcast(new PlayerInfoUpdateS2C(new[] { Entry(selfInfo) }),
            c => c.State == ConnectionState.Play);

        // The newcomer must learn existing profiles, or the tracker's spawns of them can't render.
        var others = new List<PlayerListEntry>();
        foreach (var (clientId, context) in server.Players)
            if (clientId != client.Id && context.World.Ecs.IsAlive(context.Entity))
                others.Add(Entry(context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity)));
        if (others.Count > 0)
            client.Send(new PlayerInfoUpdateS2C(others));

        // Seed the player's equipment baseline (a restored player may already wear items) - the tracker replays
        // equipment to each viewer when it spawns this player; this is the same path as any later change.
        OnInventoryChanged(self);
    }

    public static void OnLeave(Server server, NetPlayerEntityComponent info) {
        // The entity tracker removes the despawned player entity from each viewer; here we only drop the tab entry.
        server.NetServer.Broadcast(new PlayerInfoRemoveS2C(new[] { info.Uuid }), c => c.State == ConnectionState.Play);
    }

    static SpawnPlayerS2C Spawn(in NetPlayerEntityComponent info, in TransformEntityComponent pos) =>
        new(info.EntityId, info.Uuid, info.Name, pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch);

    /// <summary>Sends a player's entity spawn (+ active flags + equipment) to one viewer's client - the player
    /// dispatch used by <c>EntityTrackerSystem</c> when this player comes into that viewer's view.</summary>
    public static void SendSpawn(NetClient client, ArchWorld ecs, ArchEntity entity) {
        var info = ecs.Get<NetPlayerEntityComponent>(entity);
        var pos = ecs.Get<TransformEntityComponent>(entity);
        // The client drops a player spawn whose UUID has no tab-list entry, so (re)send the entry right before the
        // spawn. The join-time broadcast (OnJoin) populates the tab UI, but the tracker can spawn this player to a
        // viewer before that broadcast has run (the entity exists a few lines before PlayerJoined is published, and
        // the tick thread races that window) - sending it here makes the spawn self-contained. ADD_PLAYER is
        // idempotent, so the duplicate for an already-listed player is harmless.
        client.Send(new PlayerInfoUpdateS2C(new[] { Entry(info) }));
        client.Send(Spawn(info, pos));
        if (info.Flags != EntityFlags.None)
            client.Send(new EntityFlagsS2C(info.EntityId, info.Flags));
        if (ecs.Has<InventoryEntityComponent>(entity))
            foreach (var eq in Equipment(info.EntityId, ecs.Get<InventoryEntityComponent>(entity)))
                if (eq.Slot != EquipmentSlot.OffHand || CanSeeOffhand(client)) client.Send(eq);
    }
}
