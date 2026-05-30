using SharpMinerals.Items.Components;

namespace SharpMinerals.Items;

/// <summary>
/// Registry of non-block items. Block-items live in <c>BlockRegistry</c> (every block
/// is an item), so this currently holds nothing — it's the home for future pure items
/// (tools, food, …). Ids are assigned in registration order.
/// </summary>
public static class ItemRegistry {
    static readonly List<ItemType> byId = new();
    static readonly Dictionary<string, ItemType> byName = new();

    static ItemType Register(string name, int maxStackSize = 64) {
        var item = new ItemType(byId.Count, name);
        item.With(new Stackable(maxStackSize));
        byId.Add(item);
        byName[name] = item;
        return item;
    }

    public static IReadOnlyList<ItemType> All => byId;
    public static ItemType FromId(int id) => byId[id];
    public static ItemType? FromName(string name) => byName.GetValueOrDefault(name);
}
