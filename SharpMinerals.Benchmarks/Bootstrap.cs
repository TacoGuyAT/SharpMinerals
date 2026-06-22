using System.Runtime.CompilerServices;
using SharpMinerals;
using SharpMinerals.Modding;
using SharpMinerals.Vanilla;

namespace SharpMinerals.Benchmarks;

/// <summary>Loads the engine + vanilla content once before any benchmark runs, exactly as the host and the test
/// bootstrap do, so <c>BiomeRegistry.Build</c> and the surface rules resolve real <c>minecraft:*</c> blocks.</summary>
internal static class Bootstrap {
    [ModuleInitializer]
    internal static void Init() {
        var loader = new ModLoader();
        loader.TryLoad(new CoreMod());    // engine primitives first (air id 0, missing id 1, built-in entities)
        loader.TryLoad(new VanillaMod()); // minecraft:* blocks + the overworld biomes/generator
    }
}
