using SharpMinerals.Items;

namespace SharpMinerals.Network.Messages;

// Container / inventory windows. Messages carry OUR types (ItemStack); the codec maps
// to vanilla item ids. `Revision` is a version-neutral per-window sync counter - the
// JE763 codec writes it as the protocol "State ID".

// -- Clientbound ---------------------------------------------------------------
public sealed record OpenScreenS2C(int WindowId, int WindowType, string Title) : IMessage;
public sealed record SetContainerContentS2C(int WindowId, int Revision, IReadOnlyList<ItemStack> Slots, ItemStack Carried) : IMessage;
public sealed record SetContainerSlotS2C(int WindowId, int Revision, int Slot, ItemStack Data) : IMessage;
public sealed record CloseContainerS2C(int WindowId) : IMessage;
public sealed record SetHeldItemS2C(int Slot) : IMessage;

// -- Serverbound ---------------------------------------------------------------
// Click Container's changed-slot array and carried item are intentionally NOT decoded
// - the server is authoritative and recomputes state, then resyncs the client.
public sealed record ClickContainerC2S(int WindowId, int Revision, int Slot, int Button, int Mode) : IMessage;
public sealed record CloseContainerC2S(int WindowId) : IMessage;
public sealed record SetHeldItemC2S(int Slot) : IMessage;
// Stack is resolved by the codec (wire id + custom-type NBT marker -> ItemType). Slot -1 = "throw".
// A null Stack = an item this server can't represent (handler warns); an empty non-null stack = deliberate clear.
public sealed record SetCreativeModeSlotC2S(int Slot, ItemStack? Stack) : IMessage;
