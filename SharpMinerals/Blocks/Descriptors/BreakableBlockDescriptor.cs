namespace SharpMinerals.Blocks.Descriptors;

/// <summary>Marks a block as breakable in survival and carries its mining parameters: the <see cref="Hardness"/>
/// and whether a tool is required to harvest it. A block with no such descriptor is unbreakable by a survival
/// player (e.g. bedrock). <see cref="BreakTicks"/> is the bare-hand break time at 20 TPS; a hardness of 0
/// breaks instantly. Tools, haste and efficiency are not modelled yet, so this is always the bare-hand time.</summary>
public sealed class BreakableBlockDescriptor {
    // Vanilla bare-hand mining: per-tick progress = 1 / (hardness * divisor), so the break time in ticks is
    // that divisor times the hardness. The divisor is 30 when the hand can harvest the block, 100 when it can't.
    const float HarvestableTicksPerHardness = 30f;
    const float UnharvestableTicksPerHardness = 100f;

    public float Hardness { get; }

    /// <summary>Whether a specific tool is needed to harvest this block (bare hands mine it far slower).</summary>
    public bool RequiresTool { get; }

    public BreakableBlockDescriptor(float hardness, bool requiresTool) {
        Hardness = hardness;
        RequiresTool = requiresTool;
    }

    /// <summary>Bare-hand break time in ticks (0 = instant).</summary>
    public int BreakTicks => Hardness <= 0f
        ? 0
        : (int)MathF.Ceiling(Hardness * (RequiresTool ? UnharvestableTicksPerHardness : HarvestableTicksPerHardness));
}
