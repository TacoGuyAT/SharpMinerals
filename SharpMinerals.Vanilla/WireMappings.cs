using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Network;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Vanilla.Data;

namespace SharpMinerals.Vanilla;

/// <summary>
/// Registers the vanilla wire mappings (block-state / item / entity ids per protocol version) with the data-driven
/// <see cref="TypeMapper"/>. The bulk is now driven straight from <see cref="MinecraftData"/>: every registered
/// stateless block/item gets its <c>defaultState</c> / item id per version (1.20.1 -> <see cref="ProtocolJE763"/>,
/// 1.19.4 -> <see cref="ProtocolJE762"/>), which automatically tracks the state-id shifts between releases. Only
/// the engine primitives, the few stateful blocks (which need explicit striding), and the legacy flat-id 1.5.2
/// protocol are hand-written. Ranges use the protocol inheritance chain: a <c>Map&lt;ProtocolJE762&gt;</c> applies
/// to 762 AND 763 (onwards), and a <c>Map&lt;ProtocolJE763&gt;</c> delta overrides it per facet for 763+.
/// </summary>
internal static class WireMappings {
    public static void Register(MinecraftData data) {
        // -- Engine primitives (not in minecraft-data under these names) - their wire ids are vanilla facts. ----
        TypeMapper.Map<ProtocolJE762>("sharpminerals:air").State(0);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:missing").State(1).Item(1);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:item").Entity(54);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:falling_block").Entity(36);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:player").EntityViaSpawnPlayer();

        // -- Bulk (modern Java): every registered minecraft block/item, per version, from the data. -------------
        // Stateful blocks (a StatesBlockDescriptor) are skipped here and get explicit striding below; engine
        // primitives (sharpminerals namespace) are skipped too.
        foreach (var block in BlockRegistry.All) {
            if (block.Id.Namespace != "minecraft") continue;
            if (block.Has<StatesBlockDescriptor>()) continue;
            MapBlock<ProtocolJE763>(block, data.V763);
            MapBlock<ProtocolJE762>(block, data.V762);
        }
        foreach (var item in ItemRegistry.All) {
            if (item is BlockType) continue; // block items handled above
            if (item.Id.Namespace != "minecraft") continue;
            MapItem<ProtocolJE763>(item, data.V763);
            MapItem<ProtocolJE762>(item, data.V762);
        }

        // -- Stateful blocks: explicit striding the data can't express as a single default state. ----------------
        // chest: facing has stride 6 (4 facings packed in the larger state space); 762 also carries the BlockEntity
        // (1) which 763 inherits per facet. wool: the engine's synthetic Color axis maps to the 16 colour ids.
        TypeMapper.Map<ProtocolJE762>("minecraft:chest").State(2951, (State.Facing, 6)).Item(275).BlockEntity(1);
        TypeMapper.Map<ProtocolJE763>("minecraft:chest").State(2955, (State.Facing, 6)).Item(277);
        TypeMapper.Map<ProtocolJE762>("minecraft:wool").State(2043, (State.Color, 1)).Item(179, (State.Color, 1));
        TypeMapper.Map<ProtocolJE763>("minecraft:wool").State(2047, (State.Color, 1)).Item(180, (State.Color, 1));

        // -- Legacy Java (1.5.2 / protocol 61): flat ids, state == item; colour/metadata not modeled; no entities. --
        TypeMapper.Map<ProtocolJE61>("sharpminerals:air").State(0);
        TypeMapper.Map<ProtocolJE61>("sharpminerals:missing").State(1).Item(1);
        TypeMapper.Map<ProtocolJE61>("minecraft:stone").State(1).Item(1);
        TypeMapper.Map<ProtocolJE61>("minecraft:grass_block").State(2).Item(2);
        TypeMapper.Map<ProtocolJE61>("minecraft:dirt").State(3).Item(3);
        TypeMapper.Map<ProtocolJE61>("minecraft:cobblestone").State(4).Item(4);
        TypeMapper.Map<ProtocolJE61>("minecraft:bedrock").State(7).Item(7);
        TypeMapper.Map<ProtocolJE61>("minecraft:wool").State(35).Item(35);
        TypeMapper.Map<ProtocolJE61>("minecraft:chest").State(54).Item(54);
    }

    /// <summary>Maps a stateless block's default block-state id (and item id, if it has one) for one protocol from
    /// the version data. A block absent in that version (a few 1.20 additions) is left unmapped, so it falls back
    /// to <c>missing</c> (-> stone) on that protocol.</summary>
    static void MapBlock<TProto>(BlockType block, MinecraftData.JavaVersion v) where TProto : Protocol {
        if (!v.DefaultState.TryGetValue(block.Id.Name, out int state)) return;
        var b = TypeMapper.Map<TProto>(block.Id.Full).State(state);
        if (v.ItemId.TryGetValue(block.Id.Name, out int item)) b.Item(item);
    }

    static void MapItem<TProto>(ItemType item, MinecraftData.JavaVersion v) where TProto : Protocol {
        if (v.ItemId.TryGetValue(item.Id.Name, out int id))
            TypeMapper.Map<TProto>(item.Id.Full).Item(id);
    }
}
