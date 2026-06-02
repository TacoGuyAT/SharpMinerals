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

/// <summary>A block behavior that reacts to a falling-block entity of this kind landing (<see cref="BlockContext.Position"/>
/// is the resting cell; <see cref="BlockContext.Actor"/> is null). Sand re-places itself; a mod could explode instead.</summary>
public interface IOnLand { void OnLand(in BlockContext ctx); }

/// <summary>A block behavior that reacts to a redstone signal (reserved; not yet fired).</summary>
public interface IRedstoneActivated { void OnRedstoneActivated(in BlockContext ctx); }
