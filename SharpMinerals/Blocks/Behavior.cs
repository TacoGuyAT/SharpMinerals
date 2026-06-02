using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using SharpMinerals.Math;

namespace SharpMinerals.Blocks;

/// <summary>The context handed to a block behavior when an event fires: world/position, the block
/// definition, and the acting player (null when no player triggered it, e.g. gravity or a command).</summary>
public readonly struct BlockContext {
    public World World { get; init; }
    public Vector3i Position { get; init; }
    public BlockType Block { get; init; }
    public PlayerContext? Actor { get; init; }
}

/// <summary>A block behavior that reacts to a player/entity interacting with it.</summary>
public interface IInteract { void OnInteract(in BlockContext ctx); }

/// <summary>A block behavior that reacts to the block being broken.</summary>
public interface IOnBroken { void OnBroken(in BlockContext ctx); }

/// <summary>A block behavior that reacts to a redstone signal (reserved; not yet fired).</summary>
public interface IRedstoneActivated { void OnRedstoneActivated(in BlockContext ctx); }

/// <summary>Dispatches a behavior event to whichever of a block definition's components implement it.</summary>
public static class Behavior {
    public static void FireBroken(BlockType block, in BlockContext ctx) {
        foreach (var c in block.Components)
            if (c is IOnBroken handler) handler.OnBroken(in ctx);
    }

    /// <summary>Returns true if any component handled the interaction (so the caller suppresses block placement).</summary>
    public static bool FireInteract(BlockType block, in BlockContext ctx) {
        bool handled = false;
        foreach (var c in block.Components)
            if (c is IInteract handler) { handler.OnInteract(in ctx); handled = true; }
        return handled;
    }
}
