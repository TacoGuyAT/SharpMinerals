using SharpMinerals.Blocks;
using SharpMinerals.Network;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE763;

namespace SharpMinerals.Vanilla;

/// <summary>
/// Registers the vanilla wire mappings (block-state / item / entity ids per protocol version) with the data-driven
/// <see cref="TypeMapper"/>. This is the wire-side counterpart to the content registration in <see cref="VanillaMod"/>
/// — the core engine knows no vanilla ids; they all live here. Ranges use the protocol inheritance chain: a
/// <c>Map&lt;ProtocolJE762&gt;</c> applies to 762 AND 763 (onwards), and a <c>Map&lt;ProtocolJE763&gt;</c> delta
/// overrides it per facet for 763+.
/// </summary>
internal static class WireMappings {
    public static void Register() {
        // ── Modern Java (1.19.4 / protocol 762 onwards) ─────────────────────────────────────────
        // Engine primitives — their wire ids are vanilla facts (air=0, missing→stone, the entity type ids).
        TypeMapper.Map<ProtocolJE762>("sharpminerals:air").State(0);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:missing").State(1).Item(1);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:item").Entity(54);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:falling_block").Entity(36);
        TypeMapper.Map<ProtocolJE762>("sharpminerals:player").EntityViaSpawnPlayer();

        // 1.19.4 vanilla blocks/items.
        TypeMapper.Map<ProtocolJE762>("minecraft:bedrock").State(79).Item(43);
        TypeMapper.Map<ProtocolJE762>("minecraft:stone").State(1).Item(1);
        TypeMapper.Map<ProtocolJE762>("minecraft:dirt").State(10).Item(15);
        TypeMapper.Map<ProtocolJE762>("minecraft:grass_block").State(9).Item(14);
        TypeMapper.Map<ProtocolJE762>("minecraft:cobblestone").State(14).Item(22);
        TypeMapper.Map<ProtocolJE762>("minecraft:sand").State(112).Item(44);
        TypeMapper.Map<ProtocolJE762>("minecraft:red_sand").State(117).Item(46);
        TypeMapper.Map<ProtocolJE762>("minecraft:gravel").State(118).Item(47);
        TypeMapper.Map<ProtocolJE762>("minecraft:chest").State(2951, (State.Facing, 6)).Item(275).BlockEntity(1);
        TypeMapper.Map<ProtocolJE762>("minecraft:wool").State(2043, (State.Color, 1)).Item(179, (State.Color, 1));
        TypeMapper.Map<ProtocolJE762>("minecraft:stick").Item(803);

        // 1.20.1 (protocol 763) deltas — the 1.20 additions shifted these ids. Unspecified facets inherit from 762
        // (chest keeps its BlockEntity, red_sand/gravel keep their State).
        TypeMapper.Map<ProtocolJE763>("minecraft:chest").State(2955, (State.Facing, 6)).Item(277);
        TypeMapper.Map<ProtocolJE763>("minecraft:wool").State(2047, (State.Color, 1)).Item(180, (State.Color, 1));
        TypeMapper.Map<ProtocolJE763>("minecraft:red_sand").Item(47);
        TypeMapper.Map<ProtocolJE763>("minecraft:gravel").Item(48);
        TypeMapper.Map<ProtocolJE763>("minecraft:stick").Item(807);

        // ── Legacy Java (1.5.2 / protocol 61): flat ids, state == item; colour/metadata not modeled; no entities. ──
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
}
