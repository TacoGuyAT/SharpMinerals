namespace SharpMinerals.Blocks;

/// <summary>
/// A block-state property — a named axis with an ordered set of possible values (e.g.
/// <c>facing = north|south|west|east</c>). Values are addressed by index; the order
/// matches vanilla so the network layer can map a state to its vanilla id later. A
/// block declares which properties it has via a <c>StateProperties</c> component.
/// </summary>
public sealed class State {
    public string Name { get; }
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// Whether this property is part of the block's ITEM IDENTITY (kept on a dropped item) rather than
    /// PLACEMENT state (reset when the block drops). Most properties are placement (facing, axis, slab
    /// type, …); colour is the exception — each colour is effectively a distinct item.
    /// </summary>
    public bool PreservedInItem { get; }

    public State(string name, params string[] values) : this(name, false, values) { }

    public State(string name, bool preservedInItem, params string[] values) {
        Name = name;
        Values = values;
        PreservedInItem = preservedInItem;
    }

    /// <summary>Number of possible values.</summary>
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

    // Our own axis (vanilla has no "color" property — each colour is a separate block);
    // ordered to match the vanilla dye/wool order so the network layer can map by index.
    // Colour is ITEM identity — it's kept when the block drops (red wool drops red wool).
    public static readonly State Color = new("color", preservedInItem: true,
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black");
}
