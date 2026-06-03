using SharpMinerals.Blocks;
using SharpMinerals.Network.Protocols.JE762;

namespace SharpMinerals.Network.Protocols.JE763;

/// <summary>
/// <see cref="ITypeMapper"/> for Java Edition 1.20.1 (protocol 763). Identical to 1.19.4
/// (<see cref="TypeMapperJE762"/>) except for the wire ids that the 1.20 block/item additions shifted upward —
/// this overwrites just those entries and rebuilds the derived lookups. All mapping logic is inherited.
/// </summary>
public sealed class TypeMapperJE763 : TypeMapperJE762 {
    // Wool moved up one item / four block-states when the 1.20 content was inserted before it in the registries.
    protected override int WoolItemBase => 180;

    public TypeMapperJE763() {
        // Blocks added in 1.20 (bamboo/cherry/suspicious sand/…) shifted these later global-palette + item ids.
        stateByName["chest"] = 2955;
        stateByName["wool"] = 2047;
        stateLayouts["chest"] = new StateLayout(2955, (State.Facing, 6));
        stateOverrides["wool"] = s => 2047 + s.Get(State.Color); // white 2047 … black 2062

        itemIdByName["chest"] = 277;
        itemIdByName["wool"] = 180; // white_wool 180 … black_wool 195
        itemIdByName["red_sand"] = 47;
        itemIdByName["gravel"] = 48;
        itemIdByName["stick"] = 807;
        itemOverrides["wool"] = s => 180 + s.Get(State.Color);

        RebuildLookups(); // itemById + stateByBlockId were built from 1.19.4 values in the base ctor
    }
}
