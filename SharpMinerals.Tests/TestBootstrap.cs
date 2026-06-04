using System.Runtime.CompilerServices;
using SharpMinerals.Blocks;
using SharpMinerals.Vanilla;
using SharpMinerals.Modding;

namespace SharpMinerals.Tests;

/// <summary>Registers the vanilla content (the <c>minecraft</c> mod) once, before any test runs, mirroring what
/// the host does. Tests then reference <c>Vanilla.Stone</c> etc. and the type mappers resolve <c>minecraft:*</c>
/// ids. The registry is left UNFROZEN — several tests register their own content (modded blocks, items).</summary>
internal static class TestBootstrap {
    [ModuleInitializer]
    internal static void Init() {
        _ = BlockRegistry.Air;                       // engine blocks first (air id 0, missing id 1)
        new ModLoader().TryLoad(new VanillaMod()); // minecraft:* content + Vanilla.* fields
    }
}
