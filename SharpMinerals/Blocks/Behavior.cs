using SharpMinerals.Level;
using SharpMinerals.Math;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Blocks;

/// <summary>The context handed to a block behavior when an event fires: world/position, the block
/// definition, and the acting entity (default = none).</summary>
public readonly struct BlockContext {
    public World World { get; init; }
    public Vector3i Position { get; init; }
    public BlockType Block { get; init; }
    public ArchEntity Actor { get; init; }
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

    public static void FireInteract(BlockType block, in BlockContext ctx) {
        foreach (var c in block.Components)
            if (c is IInteract handler) handler.OnInteract(in ctx);
    }
}
