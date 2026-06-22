using SharpMinerals.Components;
using SharpMinerals.Items;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Containers;

/// <summary>A slot in a container window. All behaviour is defined via delegates, and can be configured fluently.</summary>
public sealed class Slot {
    public required Func<ItemStack> Get { get; set; }
    public required Action<ItemStack> Set { get; set; }
    public bool Locked { get; set; }
    public Func<ClickContainerC2S, ItemStack, ItemStack, (bool Handled, ItemStack NewSlot, ItemStack NewCursor)>? Intercept { get; set; }

    // ---- Static factories ----

    /// <summary>A normal slot backed by an inventory component at the given index.</summary>
    public static Slot Backing(InventoryComponent inv, int index) =>
        new() {
            Get = () => inv[index],
            Set = v => inv[index] = v
        };

    /// <summary>An inert locked slot (no item, no interaction).</summary>
    public static Slot Inert { get; } = new() {
        Get = () => default,
        Set = _ => { },
        Locked = true
    };

    /// <summary>A locked button slot that runs an action on click.</summary>
    public static Slot Button(Action<ClickContainerC2S> onClick) =>
        new() {
            Get = () => default,
            Set = _ => { },
            Locked = true,
            Intercept = (msg, _, cursor) => {
                onClick(msg);
                return (true, default, cursor);
            }
        };

    /// <summary>A read‑only slot that always returns the given stack.</summary>
    public static Slot Fixed(ItemStack stack, bool locked = true) =>
        new() {
            Get = () => stack,
            Set = _ => { },
            Locked = locked
        };

    /// <summary>A mutable slot storing an ItemStack in a closure.</summary>
    public static Slot Mutable(ItemStack initial) {
        var stack = initial;
        return new Slot {
            Get = () => stack,
            Set = v => stack = v
        };
    }

    /// <summary>A fully custom slot with explicit getter/setter and optional intercept.</summary>
    public static Slot Custom(Func<ItemStack> get, Action<ItemStack> set,
                              Func<ClickContainerC2S, ItemStack, ItemStack, (bool, ItemStack, ItemStack)>? intercept = null,
                              bool locked = false) =>
        new() {
            Get = get,
            Set = set,
            Locked = locked,
            Intercept = intercept
        };

    // ---- Fluent modifiers ----

    public Slot WithLocked(bool locked = true) {
        Locked = locked;
        return this;
    }

    public Slot WithIntercept(Func<ClickContainerC2S, ItemStack, ItemStack, (bool, ItemStack, ItemStack)> intercept) {
        Intercept = intercept;
        return this;
    }

    /// <summary>Wraps the current slot with a mutable closure, allowing you to set an initial value.
    /// If the slot already has a custom get/set, they are replaced with a closure that holds the given stack.</summary>
    public Slot WithInitial(ItemStack initial) {
        var stack = initial;
        Get = () => stack;
        Set = v => stack = v;
        return this;
    }
}