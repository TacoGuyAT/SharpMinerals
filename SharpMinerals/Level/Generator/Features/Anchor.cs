using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator.Features;

/// <summary>Which way a feature grows from its anchor: up from a floor (the ordinary surface case) or down from
/// a ceiling (a cave-roof feature). Reserved for the cave targets; every surface anchor is <see cref="Up"/>.</summary>
public enum AnchorFace { Up, Down }

/// <summary>A resolved spot a feature can root at in a column: the world <paramref name="Y"/> of the solid cell
/// it sits on, the <paramref name="Surface"/> block at that cell (air/unknown for a density-derived target that
/// cannot read blocks), and which way it grows (<paramref name="Face"/>). A column can yield several anchors
/// (many cave pockets); the world surface yields exactly one.</summary>
public readonly record struct Anchor(int Y, BlockType Surface, AnchorFace Face = AnchorFace.Up);
