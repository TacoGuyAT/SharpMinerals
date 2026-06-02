namespace SharpMinerals.Blocks.Descriptors;

/// <summary>Marks a block subject to gravity (sand, gravel): when it loses its support it detaches into a
/// <c>falling_block</c> entity that re-becomes a block when it lands.</summary>
public sealed class FallingBlockDescriptor { }
