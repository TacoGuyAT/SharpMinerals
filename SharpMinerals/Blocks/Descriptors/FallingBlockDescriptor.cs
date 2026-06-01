namespace SharpMinerals.Blocks.Descriptors;

/// <summary>
/// Marks a block subject to gravity (sand, gravel): when it loses its support — placed over
/// air, or the block beneath it is removed — it detaches into a <c>falling_block</c> entity that
/// falls under the normal entity physics and re-becomes a block when it lands. Pure marker, no data.
/// </summary>
public sealed class FallingBlockDescriptor { }
