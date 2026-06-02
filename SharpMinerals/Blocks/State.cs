namespace SharpMinerals.Blocks;

/// <summary>A block-state property — a named axis with an ordered set of values (e.g.
/// <c>facing = north|south|west|east</c>). Addressed by index; order matches vanilla so the network layer
/// can map a state to its vanilla id.</summary>
public sealed class State {
    public string Name { get; }
    public IReadOnlyList<string> Values { get; }

    /// <summary>Whether this property is item-identity (kept on a dropped item) rather than placement state
    /// (reset when the block drops). Most are placement; colour is the exception.</summary>
    public bool PreservedInItem { get; }

    public State(string name, params string[] values) : this(name, false, values) { }

    public State(string name, bool preservedInItem, params string[] values) {
        Name = name;
        Values = values;
        PreservedInItem = preservedInItem;
    }

    public int Count => Values.Count;

    /// <summary>The index of a value name, or -1 if not one of this property's values.</summary>
    public int IndexOf(string value) {
        for (int i = 0; i < Values.Count; i++)
            if (Values[i] == value) return i;
        return -1;
    }

    public override string ToString() => Name;

    // ── Reusable vanilla-aligned properties ──────────────────────────────────
    public static readonly State Facing = new("facing", "north", "south", "west", "east");
    public static readonly State SlabType = new("type", "bottom", "top", "double");
    public static readonly State Axis = new("axis", "x", "y", "z");
    public static readonly State Waterlogged = new("waterlogged", "true", "false");

    // Our own axis (vanilla has no "color" property), ordered to match vanilla dye/wool order so the
    // network layer can map by index. Item-identity: kept when the block drops.
    public static readonly State Color = new("color", preservedInItem: true,
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black");
}
