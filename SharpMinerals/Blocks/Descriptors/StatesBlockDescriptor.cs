namespace SharpMinerals.Blocks.Descriptors;

/// <summary>
/// The state properties a block has (chest <c>facing</c>, log <c>axis</c>, slab <c>type</c>,
/// <c>waterlogged</c>, fence connections, …). A block can have any number; order matters —
/// it matches vanilla so the network layer can flatten a state to a vanilla state id.
/// </summary>
public sealed class StatesBlockDescriptor {
    public IReadOnlyList<State> States { get; }
    public StatesBlockDescriptor(params State[] states) => States = states;

    /// <summary>The position of a property in this set, or -1 if absent.</summary>
    public int IndexOf(State state) {
        for (int i = 0; i < States.Count; i++)
            if (States[i] == state) return i;
        return -1;
    }
}
