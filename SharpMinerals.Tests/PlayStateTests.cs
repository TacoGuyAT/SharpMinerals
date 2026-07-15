using System.Collections.Concurrent;
using Arch.Core;
using SharpMinerals;
using Brigadier.NET.Builder;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Vanilla;
using SharpMinerals.Vanilla.Generator;
using SharpMinerals.Commands;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;
using SharpMinerals.Math;
using SharpMinerals.Modding;
using SharpMinerals.Network;
using SharpMinerals.Network.Nbt;
using NuGet.Versioning;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Events;
using SharpMinerals.Network.Messages;
using SharpMinerals.Chat;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Network.Protocols.JE762.Codecs;
using SharpMinerals.Persistence;
using Xunit;
using World = SharpMinerals.Level.World;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Tests;

/// <summary>
/// In-process verification of the play state: flat generation, block break/place, item
/// drops, codec round-trips, and the login -> dig -> place -> container -> pickup handler flow
/// driven through an in-memory transport. Ported from the old <c>dotnet run -- selftest</c>.
/// </summary>
public class PlayStateTests {
    // The JE763 type mapper, now exposed per-protocol (was a static class).
    static readonly TypeMapper Types = new TypeMapper(typeof(ProtocolJE763));

    // A player join fires fire-and-forget async-void column streaming (Streaming.SendColumnWhenReady): each op
    // awaits a background chunk load, then a scheduler-deferred publish that actually sends the column and lets
    // the tracker spawn its contents to the viewer. The real server pumps this every tick; a test must drive it.
    // Settle pumps the scheduler until the deferred queue stays quiet, so the streaming completes (columns load
    // for the viewer) AND xUnit's AsyncTestSyncContext sees the async-void ops finish (otherwise it waits on them
    // forever and the test hangs). Bounded; flat-gen column loads settle in a few ms.
    static void Settle(Server server) {
        long deadline = Environment.TickCount64 + 5000;
        int quiet = 0;
        while (Environment.TickCount64 < deadline && quiet < 50) {
            if (server.Scheduler.HasDeferred) { server.Scheduler.Run(); quiet = 0; }
            else quiet++;
            Thread.Sleep(1);
        }
        server.Scheduler.Run();
    }

    // Guarantees the join-triggered async-void streaming ops can never gate test completion: called BEFORE a
    // login, it detaches this thread from xUnit's AsyncTestSyncContext, so the ops (created during the login)
    // are never counted and the test can't hang waiting on them - independent of how far Settle's pump got. The
    // streaming still runs (Settle drives it) for the assertions that need the columns loaded.
    static void DetachSyncContext() => System.Threading.SynchronizationContext.SetSynchronizationContext(null);

    // -- Flat generation ----------------------------------------------------
    [Fact]
    public void FlatGeneration() {
        var gen = new World("gen", new FlatChunkGenerator());
        Assert.True(gen.GetBlock(new Vector3i(0, 0, 0)) == VanillaMod.Bedrock, "flat: bedrock at y=0");
        Assert.True(gen.GetBlock(new Vector3i(0, 2, 0)) == VanillaMod.Dirt, "flat: dirt at y=2");
        Assert.True(gen.GetBlock(new Vector3i(0, 4, 0)) == VanillaMod.GrassBlock, "flat: grass at y=4");
        Assert.True(gen.GetBlock(new Vector3i(0, 5, 0)).IsAir, "flat: air at y=5");
        Assert.True(gen.GetBlock(new Vector3i(-1, 0, -33)) == VanillaMod.Bedrock, "flat: works at negative coords");
    }

    // -- Procedural overworld generation ------------------------------------
    [Fact]
    public void SplineInterpolatesAndClampsEnds() {
        var s = new Spline((0.0, 0.0), (1.0, 10.0));
        Assert.Equal(0.0, s.Sample(-5));   // clamped flat below the first point
        Assert.Equal(10.0, s.Sample(5));   // clamped flat above the last point
        Assert.Equal(5.0, s.Sample(0.5), 6);
    }

    [Fact]
    public void MathUtilHelpers() {
        Assert.Equal(5.0, MathUtil.Lerp(0, 10, 0.5));
        Assert.Equal(2.0, MathUtil.Clamp(5, 0, 2));
        Assert.Equal(0.5, MathUtil.InverseLerp(0, 10, 5));
        Assert.Equal(0.0, MathUtil.Smoothstep(-1));
        Assert.Equal(1.0, MathUtil.Smoothstep(2));
    }

    [Fact]
    public void OverworldGenerationIsDeterministicForSeed() {
        var a = new World("ovw-a", OverworldChunkGenerator.Create(99));
        var b = new World("ovw-b", OverworldChunkGenerator.Create(99));
        for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                Assert.True(a.GetBlock(new Vector3i(x, 64, z)) == b.GetBlock(new Vector3i(x, 64, z)),
                    "overworld: same seed yields the same block");
    }

    [Fact]
    public void RequestChunkLoadsAsyncAndMatchesSyncGeneration() {
        var pos = new Vector3i(3, 4, -2);
        // The expected content from the blocking path (a fresh world so no shared cache colours the result).
        var sync = new World("ovw-sync", OverworldChunkGenerator.Create(99));
        var expected = sync.GetChunk(pos);

        var asyncWorld = new World("ovw-async", OverworldChunkGenerator.Create(99));
        Assert.False(asyncWorld.TryGetLoaded(pos, out _), "not loaded before it is requested");
        Assert.True(asyncWorld.RequestChunk(pos), "first request enqueues");
        Assert.False(asyncWorld.RequestChunk(pos), "a duplicate request while in flight is a no-op");

        SharpMinerals.Level.Chunk loaded = null!;
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline && !asyncWorld.TryGetLoaded(pos, out loaded!))
            Thread.Sleep(5);
        Assert.NotNull(loaded); // the background worker published it

        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    Assert.True(loaded.GetBlock(x, y, z) == expected.GetBlock(x, y, z),
                        "async-loaded chunk matches the synchronously generated one");
        asyncWorld.Unload();
        sync.Unload();
    }

    [Fact]
    public void IsColumnLoadedTracksTheWholeHeight() {
        var world = new World("ovw-col", OverworldChunkGenerator.Create(7));
        Assert.False(world.IsColumnLoaded(0, 0), "a fresh column is not loaded");
        world.RequestColumn(0, 0);
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline && !world.IsColumnLoaded(0, 0))
            Thread.Sleep(5);
        Assert.True(world.IsColumnLoaded(0, 0), "every cube-chunk of the column finished loading");
        world.Unload();
    }

    // A world with only the terrain + surface shaders - no water fill and no decorators - so tests of the bare
    // heightfield are not perturbed by water filling carved channels or by trees/ground cover above the surface.
    static World TerrainOnlyWorld(string name, int seed, out BiomeSource source, out IDensity density) {
        source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        density = new TrilinearDensity(new BiomeDensity(seed, source));
        var gen = new ShaderChunkGenerator(new TerrainShader(density), new SurfaceShader(density, source));
        return new World(name, gen);
    }

    [Fact]
    public void OverworldSurfaceMatchesBiomeRule() {
        int seed = 1337;
        var world = TerrainOnlyWorld("ovw-surf", seed, out var source, out _);
        // Pick a column with a clean (non-overhang) surface, then check it is capped by its biome's surface
        // top block, with air above and stone well below the soil - whichever biome owns the column.
        for (int x = 0; x < 16; x++) {
            int top = TopSolidY(world, x, 0);
            if (top <= 0 || !SolidColumnBelow(world, x, 0, top, 8)) continue;
            if (source.StripSoil(x, 0)) continue; // rocky columns are bare stone, not the biome's soil surface
            var biome = source.SurfacePick(x, 0);
            var expectedTop = biome.Surface.Block(x, top, 0, depthBelowSurface: 0, VanillaMod.Stone);
            Assert.True(world.GetBlock(new Vector3i(x, top, 0)) == expectedTop, $"overworld: top is {biome.Name}'s surface");
            Assert.True(world.GetBlock(new Vector3i(x, top + 1, 0)).IsAir, "overworld: above the surface is air");
            Assert.True(world.GetBlock(new Vector3i(x, top - 8, 0)) == VanillaMod.Stone, "overworld: deep is stone");
            return;
        }
        Assert.Fail("overworld: no clean surface column found in the test row");
    }

    [Fact]
    public void BiomeWireIdsMatchRegistrationOrder() {
        Assert.Equal(0, BiomeWireRegistry.IdOf("plains"));   // plains first -> the client's default biome
        Assert.Equal(1, BiomeWireRegistry.IdOf("forest"));
        Assert.Equal(2, BiomeWireRegistry.IdOf("badlands"));
        Assert.Equal(3, BiomeWireRegistry.IdOf("ocean"));
        Assert.Equal(4, BiomeWireRegistry.IdOf("beach"));
        Assert.Equal(0, BiomeWireRegistry.IdOf(null));       // no-biome world -> default
        Assert.Equal(0, BiomeWireRegistry.IdOf("nonsense")); // unknown -> default
        Assert.NotNull(RegistryCodec.Default);               // login biome registry NBT builds without throwing
    }

    [Fact]
    public void OverworldChunkPacketEncodesWithBiomes() {
        // End-to-end: generate + serialize a column, exercising the interpolated density, block-state mapping,
        // and the new per-cell biome palette. Just has to produce a non-empty, exception-free packet.
        var world = new World("ovw-pkt", OverworldChunkGenerator.Create(1337));
        var types = new TypeMapper(typeof(ProtocolJE763));
        var packet = ChunkSerializer.Build(types, world, 0, 0);
        Assert.True(packet.Payload.Length > 0, "chunk packet encodes");
    }

    [Fact]
    public void GrassSurfaceBecomesDirtUnderwater() {
        var rule = new LayeredSurfaceRule(VanillaMod.GrassBlock, VanillaMod.Dirt, fillerDepth: 3, submergedTop: VanillaMod.Dirt);
        Assert.Equal(VanillaMod.GrassBlock, rule.Block(0, 63, 0, 0, VanillaMod.Stone)); // at the waterline, dry -> grass
        Assert.Equal(VanillaMod.Dirt, rule.Block(0, 62, 0, 0, VanillaMod.Stone));       // water above -> dirt
        Assert.Equal(VanillaMod.Dirt, rule.Block(0, 70, 0, 2, VanillaMod.Stone));       // within filler depth -> dirt
        Assert.Equal(VanillaMod.Stone, rule.Block(0, 70, 0, 5, VanillaMod.Stone));      // below soil -> unchanged

        // Sand floors are not swapped underwater (no submergedTop).
        var sand = new LayeredSurfaceRule(VanillaMod.Sand, VanillaMod.Dirt, fillerDepth: 2);
        Assert.Equal(VanillaMod.Sand, sand.Block(0, 30, 0, 0, VanillaMod.Stone));       // deep ocean floor stays sand
    }

    [Fact]
    public void BiomeTreeDensityByBiome() {
        var biomes = BiomeRegistry.Build(1);
        double Density(string name) {
            foreach (var b in biomes) if (b.Name == name) return b.TreeDensity;
            return -1.0;
        }
        Assert.True(Density("forest") > Density("plains"), "forest is denser than plains");
        Assert.True(Density("plains") > 0.0, "plains has some trees");
        Assert.Equal(0.0, Density("badlands"));
        Assert.Equal(0.0, Density("ocean"));
    }

    [Fact]
    public void FeatureDensityStaysInRange() {
        var source = new BiomeSource(1, BiomeRegistry.Build(1));
        for (int i = 0; i < 50; i++)
            Assert.InRange(source.FeatureDensity(i * 37, i * 71), 0.0, 1.0);
    }

    [Fact]
    public void ForestsGrowTrees() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var world = new World("ovw-trees", OverworldChunkGenerator.Create(seed));

        // Climate lookup is pure noise (no chunk gen), so scan cheaply for a forest, then probe the terrain there.
        int fx = 0, fz = 0;
        bool found = false;
        for (int x = 0; x < 12000 && !found; x += 16)
            for (int z = 0; z < 12000 && !found; z += 16)
                if (source.Dominant(x, z).Name == "forest") { fx = x; fz = z; found = true; }
        Assert.True(found, "trees: found a forest");

        int logs = 0;
        for (int x = fx; x < fx + 32; x++)
            for (int z = fz; z < fz + 32; z++)
                for (int y = 60; y < 130; y++)
                    if (world.GetBlock(new Vector3i(x, y, z)) == VanillaMod.OakLog) logs++;
        Assert.True(logs > 0, $"trees: a forest area has oak logs (found {logs})");
    }

    [Fact]
    public void GroundCoverGrowsOnGrassyBiomes() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var world = new World("ovw-plants", OverworldChunkGenerator.Create(seed));

        int fx = 0, fz = 0;
        bool found = false;
        for (int x = 0; x < 12000 && !found; x += 16)
            for (int z = 0; z < 12000 && !found; z += 16)
                if (source.Dominant(x, z).Name == "forest") { fx = x; fz = z; found = true; }
        Assert.True(found, "plants: found a forest");

        int grass = 0;
        for (int x = fx; x < fx + 48; x++)
            for (int z = fz; z < fz + 48; z++)
                for (int y = 60; y < 130; y++)
                    if (world.GetBlock(new Vector3i(x, y, z)) == VanillaMod.ShortGrass) grass++;
        Assert.True(grass > 0, $"plants: a forest has short grass (found {grass})");

        // The forest chunk serializes - exercises the new plant state ids end to end.
        var packet = ChunkSerializer.Build(new TypeMapper(typeof(ProtocolJE763)), world, fx >> 4, fz >> 4);
        Assert.True(packet.Payload.Length > 0, "plants: forest chunk encodes");

        // Badlands (red sand, no grass block) gets no ground cover.
        int bx = 0, bz = 0;
        bool badlands = false;
        for (int x = 0; x < 12000 && !badlands; x += 16)
            for (int z = 0; z < 12000 && !badlands; z += 16)
                if (source.Dominant(x, z).Name == "badlands") { bx = x; bz = z; badlands = true; }
        if (badlands) {
            int onSand = 0;
            for (int x = bx; x < bx + 32; x++)
                for (int z = bz; z < bz + 32; z++)
                    for (int y = 60; y < 140; y++)
                        if (world.GetBlock(new Vector3i(x, y, z)) == VanillaMod.ShortGrass) onSand++;
            Assert.Equal(0, onSand);
        }
    }

    [Fact]
    public void BadlandsGrowDeadBushes() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var world = new World("ovw-deadbush", OverworldChunkGenerator.Create(seed));

        int bx = 0, bz = 0;
        bool found = false;
        for (int x = 0; x < 12000 && !found; x += 16)
            for (int z = 0; z < 12000 && !found; z += 16)
                if (source.Dominant(x, z).Name == "badlands") { bx = x; bz = z; found = true; }
        Assert.True(found, "deadbush: found badlands");

        int bushes = 0;
        for (int x = bx; x < bx + 64; x++)
            for (int z = bz; z < bz + 64; z++)
                for (int y = 60; y < 160; y++)
                    if (world.GetBlock(new Vector3i(x, y, z)) == VanillaMod.DeadBush) bushes++;
        Assert.True(bushes > 0, $"deadbush: badlands has dead bushes (found {bushes})");
    }

    [Fact]
    public void TreesAreSpacedApart() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var world = new World("ovw-spacing", OverworldChunkGenerator.Create(seed));
        int fx = 0, fz = 0;
        bool found = false;
        for (int x = 0; x < 12000 && !found; x += 16)
            for (int z = 0; z < 12000 && !found; z += 16)
                if (source.Dominant(x, z).Name == "forest") { fx = x; fz = z; found = true; }
        Assert.True(found, "spacing: found a forest");

        // Each tree is a single trunk column, so distinct columns containing a log are distinct trees.
        var trunks = new System.Collections.Generic.List<(int X, int Z)>();
        for (int x = fx; x < fx + 48; x++)
            for (int z = fz; z < fz + 48; z++)
                for (int y = 60; y < 130; y++)
                    if (world.GetBlock(new Vector3i(x, y, z)) == VanillaMod.OakLog) { trunks.Add((x, z)); break; }
        Assert.True(trunks.Count >= 2, $"spacing: enough trees to test ({trunks.Count})");

        int minDistance = int.MaxValue;
        for (int i = 0; i < trunks.Count; i++)
            for (int j = i + 1; j < trunks.Count; j++) {
                int d = System.Math.Max(System.Math.Abs(trunks[i].X - trunks[j].X), System.Math.Abs(trunks[i].Z - trunks[j].Z));
                if (d < minDistance) minDistance = d;
            }
        // Trunks at least CanopyRadius+1 apart -> no trunk sits inside another canopy (no leaf-through-log).
        Assert.True(minDistance >= 3, $"spacing: trunks should be >= 3 apart (min was {minDistance})");
    }

    [Fact]
    public void RiversCarveWaterThroughLand() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var world = new World("ovw-river", OverworldChunkGenerator.Create(seed));

        // Find a lowland river-centre column in a grassy biome that carved below sea level; water there (with open
        // air above) proves a river cut through land that would otherwise be dry grass.
        bool found = false;
        for (int x = 0; x < 12000 && !found; x += 4)
            for (int z = 0; z < 12000 && !found; z += 4) {
                string biome = source.Dominant(x, z).Name;
                if ((biome != "plains" && biome != "forest") || System.Math.Abs(source.River(x, z)) >= 0.003) continue;
                if (world.GetBlock(new Vector3i(x, WorldDefaults.SeaLevel - 2, z)) != VanillaMod.Water) continue;
                Assert.True(world.GetBlock(new Vector3i(x, WorldDefaults.SeaLevel, z)).IsAir, "river: open air above the water");
                found = true;
            }
        Assert.True(found, "river: a lowland river carves water through grass");
    }

    // A base terrain that is solid everywhere, so a carve's effect (and only the carve) is what shows through.
    sealed class SolidDensity : IDensity {
        public const double Value = 1000.0;
        public double At(int x, int y, int z) => Value;
    }

    [Fact]
    public void RockyPatchesStripSoilToBareStone() {
        int seed = 1337;
        var world = TerrainOnlyWorld("ovw-rocky", seed, out var source, out _);
        var heights = new BiomeDensity(seed, source); // same seed -> same surface as the world (cheap, no chunk gen)

        // Cheaply (no chunk gen) find one stripped + one non-stripped dry-land column, then generate just those two.
        (int X, int Z)? rocky = null, soft = null;
        for (int x = 0; x < 4000 && (rocky is null || soft is null); x += 5)
            for (int z = 0; z < 4000 && (rocky is null || soft is null); z += 5) {
                if (heights.SurfaceHeight(x, z) < WorldDefaults.SeaLevel + 4) continue; // dry land, surface above water
                if (source.StripSoil(x, z)) rocky ??= (x, z);
                else soft ??= (x, z);
            }
        Assert.True(rocky is not null && soft is not null, "found both a stripped and a non-stripped dry column");

        // The stripped column is bare stone; the non-stripped one kept its soil cap - so stripping is patchy, not global.
        var (rx, rz) = rocky!.Value;
        Assert.True(world.GetBlock(new Vector3i(rx, TopSolidY(world, rx, rz), rz)) == VanillaMod.Stone,
            "stripped column surface is bare stone");
        var (sx, sz) = soft!.Value;
        Assert.True(world.GetBlock(new Vector3i(sx, TopSolidY(world, sx, sz), sz)) != VanillaMod.Stone,
            "non-stripped column kept its soil surface");

        world.Unload();
    }

    [Fact]
    public void RavinesCarveDeepNarrowChasms() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var natural = new BiomeDensity(seed, source);
        var ravine = new RavineDensity(new SolidDensity(), natural, seed);

        int carved = 0, columns = 0;
        // Scan wide enough to cross several of the low-frequency ravine "regions" (their wavelength is hundreds of
        // blocks), so this doesn't depend on whether one small window happens to sit in a ravine zone.
        for (int x = 0; x < 3000; x += 4)
            for (int z = 0; z < 3000; z += 4) {
                columns++;
                double surface = natural.SurfaceHeight(x, z);

                // The carve never touches cells well above the natural surface.
                Assert.Equal(SolidDensity.Value, ravine.At(x, (int)surface + 24, z), 6);

                // A cell in the chasm body, where a ravine runs, is opened (density driven below zero = air); and any
                // opened cell must sit between the floor and the surface, never above it.
                for (int y = 12; y < surface; y += 4) {
                    double d = ravine.At(x, y, z);
                    if (d < SolidDensity.Value - 1e-6)
                        Assert.True(y <= surface, $"ravine: carve at y={y} stays at or below the surface");
                    if (d < 0.0) carved++;
                }
            }
        Assert.True(carved > 0, $"ravine: at least one chasm carves over {columns} scanned columns");
    }

    [Fact]
    public void BiomeSelectionFollowsClimate() {
        var source = new BiomeSource(7, BiomeRegistry.Build(7));
        Assert.Equal("ocean", source.DominantForClimate(new ClimatePoint(0.0, 0.0, -0.9, -0.3, 0.0)).Name);
        Assert.Equal("plains", source.DominantForClimate(new ClimatePoint(0.1, 0.0, 0.4, -0.2, 0.0)).Name);
        Assert.Equal("forest", source.DominantForClimate(new ClimatePoint(0.0, 0.6, 0.4, 0.0, 0.0)).Name);
        Assert.Equal("badlands", source.DominantForClimate(new ClimatePoint(0.7, -0.7, 0.4, 0.2, 0.0)).Name);
    }

    [Fact]
    public void BeachIsAnElevationGatedShoreline() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed), BiomeRegistry.BuildCoastal(seed));
        source.UseSurfaceHeight(new BiomeDensity(seed, source).SurfaceHeight); // coastline detection needs the height field

        // Beach is NOT a climate-territory biome (so it never claims a wide continentalness band or shapes terrain).
        Assert.DoesNotContain(source.Biomes, b => b.Name == "beach");

        // High inland columns are never beach, whatever the climate - the elevation gate excludes them.
        for (int x = 0; x < 128; x++)
            Assert.NotEqual("beach", source.SurfaceBiomeAt(x, 0, WorldDefaults.SeaLevel + 80).Name);

        // A deep-inland column (well into a land biome, no ocean nearby) is NOT beach even at sea-level elevation -
        // proving placement is by OCEAN PROXIMITY, not merely elevation (the bug this fix targets).
        bool checkedInland = false;
        for (int x = 0; x < 8000 && !checkedInland; x += 7)
            for (int z = 0; z < 8000 && !checkedInland; z += 7)
                if (source.Dominant(x, z).Name == "badlands") { // badlands sits at high continentalness => far from ocean
                    Assert.NotEqual("beach", source.SurfaceBiomeAt(x, z, WorldDefaults.SeaLevel).Name);
                    checkedInland = true;
                }
        Assert.True(checkedInland, "found a deep-inland column to check");

        // Somewhere a warm shoreline column (ocean within reach) IS surfaced as beach; the same column raised well
        // above sea level is not.
        bool foundShorelineBeach = false;
        for (int x = 0; x < 4000 && !foundShorelineBeach; x += 3)
            for (int z = 0; z < 4000 && !foundShorelineBeach; z += 3)
                if (source.SurfaceBiomeAt(x, z, WorldDefaults.SeaLevel).Name == "beach") {
                    Assert.NotEqual("beach", source.SurfaceBiomeAt(x, z, WorldDefaults.SeaLevel + 80).Name);
                    foundShorelineBeach = true;
                }
        Assert.True(foundShorelineBeach, "a warm shoreline column with ocean nearby is surfaced as beach");
    }

    [Fact]
    public void TreesDoNotGrowOnBeaches() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed), BiomeRegistry.BuildCoastal(seed));
        var heights = new BiomeDensity(seed, source);
        source.UseSurfaceHeight(heights.SurfaceHeight);
        var world = new World("ovw-beach-trees", OverworldChunkGenerator.Create(seed));

        int verified = 0;
        for (int x = 0; x < 5000 && verified < 12; x += 6)
            for (int z = 0; z < 5000 && verified < 12; z += 6) {
                // Cheap 2D pre-filter so we only generate chunks at likely beach columns.
                if (!source.IsCoastal(x, z, (int)heights.SurfaceHeight(x, z))) continue;

                int top = TopSolidY(world, x, z);
                if (world.GetBlock(new Vector3i(x, top, z)) != VanillaMod.Sand) continue; // dry beach surface only
                if (!source.IsCoastal(x, z, top)) continue;                                // confirm against real surface

                Assert.True(world.GetBlock(new Vector3i(x, top + 1, z)) != VanillaMod.OakLog,
                    $"no tree trunk roots on the beach at ({x},{top + 1},{z})");
                verified++;
            }
        Assert.True(verified > 0, "found dry beach columns to verify");
    }

    [Fact]
    public void OceanBasinsFillWithWaterToSeaLevel() {
        int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var world = new World("ovw-water", OverworldChunkGenerator.Create(seed));

        // Climate lookup is pure noise (no chunk gen), so scan cheaply for a deep-ocean column, then probe it.
        int ox = 0, oz = 0;
        bool found = false;
        for (int x = 0; x < 12000 && !found; x += 64)
            for (int z = 0; z < 12000 && !found; z += 64)
                if (source.Dominant(x, z).Name == "ocean") { ox = x; oz = z; found = true; }
        Assert.True(found, "ocean: found a deep-ocean column");

        Assert.True(world.GetBlock(new Vector3i(ox, WorldDefaults.SeaLevel - 1, oz)) == VanillaMod.Water,
            "ocean: water just below sea level");
        Assert.True(world.GetBlock(new Vector3i(ox, WorldDefaults.SeaLevel, oz)).IsAir,
            "ocean: air at and above sea level");
        var floor = world.GetBlock(new Vector3i(ox, 12, oz));
        Assert.True(!floor.IsAir && floor != VanillaMod.Water, "ocean: solid ground below the water");
    }

    [Fact]
    public void OverworldBlocksMatchGlobalDensityAcrossCubeBorder() {
        // Solidity must equal the single global density across the x=15|16 cube border (and into the next
        // cube), proving the dispatcher feeds correct world coordinates - a seam would surface as a mismatch.
        int seed = 123;
        // Decorator-free world sharing the exact density instance, so solidity must match it cell for cell.
        var world = TerrainOnlyWorld("ovw-seam", seed, out _, out var density);
        for (int x = 14; x <= 18; x++)
            for (int y = 56; y <= 96; y++) {
                bool solid = !world.GetBlock(new Vector3i(x, y, 0)).IsAir;
                Assert.True((density.At(x, y, 0) > 0) == solid, $"overworld: density matches block at ({x},{y},0)");
            }
    }

    [Fact]
    public void NewPlayerSurfaceSpawnSitsOnSolidGround() {
        var world = new World("ovw-spawn", OverworldChunkGenerator.Create(1337));
        var spawn = world.SurfaceSpawn(0.5, 0.5);
        int sy = (int)spawn.Y;
        Assert.True(world.GetBlock(new Vector3i(0, sy, 0)).IsAir, "spawn: the spawn cell is air");
        Assert.False(world.GetBlock(new Vector3i(0, sy - 1, 0)).IsAir, "spawn: the block under the spawn is solid");
        Assert.True(sy > WorldDefaults.SurfaceY, "spawn: procedural surface is well above the flat default");
    }

    [Fact]
    public void TpsTrackerMeasuresRollingWindows() {
        var t = new TpsTracker(20.0);
        long now = 0;
        for (int i = 0; i < 20 * 120; i++) { now += 50; t.Record(now); } // a steady 20 TPS for 2 minutes
        Assert.Equal(20.0, t.Measure(now, 10), 1);   // 10s window
        Assert.Equal(20.0, t.Measure(now, 60), 1);   // 1m window
        Assert.Equal(20.0, t.Measure(now, 300), 1);  // 5m window (not yet full -> divides by elapsed, still 20)
    }

    [Fact]
    public void TpsTrackerReflectsSlowTicksAndCapsAtTarget() {
        // 10 ticks/sec for 30s: half the target should show through every window.
        var slow = new TpsTracker(20.0);
        long now = 0;
        for (int i = 0; i < 10 * 30; i++) { now += 100; slow.Record(now); }
        Assert.Equal(10.0, slow.Measure(now, 10), 1);
        Assert.Equal(10.0, slow.Measure(now, 60), 1); // window longer than history -> divides by elapsed

        // A second with far more ticks than the target (a catch-up burst) is clamped per second.
        var fast = new TpsTracker(20.0);
        long f = 0;
        for (int i = 0; i < 100 * 20; i++) { f += 10; fast.Record(f); } // 100 TPS for 20s
        Assert.Equal(20.0, fast.Measure(f, 10), 1);

        Assert.Equal(0.0, new TpsTracker(20.0).Measure(0, 10)); // no ticks recorded yet
    }

    [Fact]
    public void TpsTrackerKeepsLagVisibleAfterCatchUpBurst() {
        // The loop catches up after a stall by bursting ticks; the metric must not let that mask the lag.
        var t = new TpsTracker(20.0);
        long now = 0;
        for (int i = 0; i < 20 * 60; i++) { now += 50; t.Record(now); } // 60s healthy at 20 TPS
        now += 10_000;                                                  // a 10s stall (no ticks recorded)
        for (int i = 0; i < 200; i++) { now += 1; t.Record(now); }      // catch-up burst: 200 ticks in ~0.2s

        Assert.True(t.Measure(now, 10) < 5.0, "10s window reflects the recent stall, not the burst");
        double m1 = t.Measure(now, 60);
        Assert.True(m1 is > 12.0 and < 19.0, $"1m window averages the stall in (was {m1})");
    }

    // A multilinear field (no cross terms), which trilinear interpolation must reproduce exactly.
    sealed class LinearDensity : IDensity {
        public double At(int x, int y, int z) => 0.5 * x + 1.0 * y - 0.3 * z + 2.0;
    }

    [Fact]
    public void InterpolatedDensityIsExactForLinearFields() {
        var inner = new LinearDensity();
        var interp = new TrilinearDensity(inner);
        var tricubic = new TricubicDensity(inner);
        (int X, int Y, int Z)[] points = { (1, 2, 3), (7, 5, 11), (15, 15, 15), (-3, 40, -9), (33, 1, 128) };
        foreach (var (x, y, z) in points) {
            Assert.Equal(inner.At(x, y, z), interp.At(x, y, z), 6);   // trilinear: exact off the lattice too
            Assert.Equal(inner.At(x, y, z), tricubic.At(x, y, z), 6); // Catmull-Rom: also reproduces linear fields
        }
    }

    static int TopSolidY(World world, int x, int z) {
        for (int y = 200; y >= -64; y--)
            if (!world.GetBlock(new Vector3i(x, y, z)).IsAir) return y;
        return int.MinValue;
    }

    static bool SolidColumnBelow(World world, int x, int z, int top, int depth) {
        for (int d = 1; d <= depth; d++)
            if (world.GetBlock(new Vector3i(x, top - d, z)).IsAir) return false;
        return true;
    }

    // -- Block break + drop, then placement ----------------------------------
    [Fact]
    public void BlockBreakPlaceAndDrops() {
        var gen = new World("gen", new FlatChunkGenerator());

        int before = DropCount(gen);
        var broken = gen.BreakBlock(new Vector3i(5, 4, 5));
        Assert.True(broken == VanillaMod.GrassBlock, "break: returned the grass block");
        Assert.True(gen.GetBlock(new Vector3i(5, 4, 5)).IsAir, "break: space is now air");
        Assert.True(DropCount(gen) == before + 1, "break: spawned one drop entity");
        Assert.True(gen.BreakBlock(new Vector3i(5, 50, 5)).IsAir, "break: air yields nothing");

        Assert.True(gen.PlaceBlock(new Vector3i(5, 5, 5), VanillaMod.Stone), "place: into air succeeds");
        Assert.True(gen.GetBlock(new Vector3i(5, 5, 5)) == VanillaMod.Stone, "place: block was set");
        Assert.True(!gen.PlaceBlock(new Vector3i(5, 5, 5), VanillaMod.Dirt), "place: into a solid fails");
    }

    // -- Placement can't put a block inside a standing entity (player collision box) --
    [Fact]
    public void BlockPlacementBlockedByStandingPlayer() {
        var world = new World("place_collision", new FlatChunkGenerator());
        world.SpawnPlayer(1, "Steve", Guid.NewGuid(), 1); // feet at (0.5, SurfaceY, 0.5); box overlaps the cube at (0, SurfaceY, 0)

        var inside = new Vector3i(0, WorldDefaults.SurfaceY, 0);
        Assert.False(world.PlaceBlock(inside, VanillaMod.Stone), "can't place into the player's collision box");
        Assert.True(world.GetBlock(inside).IsAir, "nothing was placed");

        // The hitbox is the true 0.6-wide player box, not the larger pickup-reach collider, so the cell right
        // next to the player (X [1,2] vs the player's [0.2,0.8]) is clear - you can build alongside yourself.
        var adjacent = new Vector3i(1, WorldDefaults.SurfaceY, 0);
        Assert.True(world.PlaceBlock(adjacent, VanillaMod.Dirt), "placing in the adjacent cell (clear of the hitbox) succeeds");

        var clear = new Vector3i(10, WorldDefaults.SurfaceY, 10);
        Assert.True(world.PlaceBlock(clear, VanillaMod.Stone), "placing clear of any entity succeeds");
        Assert.Equal(VanillaMod.Stone, world.GetBlock(clear));
    }

    // -- A falling block (Physics | Placement) also blocks placement while it's mid-fall --
    [Fact]
    public void FallingBlockBlocksPlacement() {
        var world = new World("falling_place", new VoidChunkGenerator());
        var cell = new Vector3i(0, 10, 0);
        world.SpawnFallingBlock(cell, VanillaMod.Sand); // occupies the cell (0.98 box, Physics | Placement)

        Assert.False(world.PlaceBlock(cell, VanillaMod.Stone), "can't place into a falling block");
        Assert.True(world.PlaceBlock(new Vector3i(2, 10, 0), VanillaMod.Stone), "the cell beside it is clear");
    }

    // -- Position packing ----------------------------------------------------
    [Fact]
    public void PositionPacking() {
        foreach (var p in new[] {
            new Vector3i(0, 0, 0), new Vector3i(1, 2, 3),
            new Vector3i(-30_000_000, -2000, 30_000_000),
        }) {
            using var ms = new MinecraftStream(new MemoryStream());
            ms.WritePosition(p.X, p.Y, p.Z);
            ms.Position = 0;
            var (x, y, z) = ms.ReadPosition();
            Assert.True(x == p.X && y == p.Y && z == p.Z, $"position packs {p}");
        }
    }

    // -- Codec round-trips ---------------------------------------------------
    [Fact]
    public void CodecRoundTrips() {
        var protocol = new ProtocolJE763();
        Assert.True(RoundTrip(protocol, ConnectionState.Handshaking,
            new HandshakeC2S(763, "localhost", 25565, 2)), "round-trip Handshake");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new PlayerActionC2S(2, new Vector3i(10, -5, 20), 1, 7)), "round-trip PlayerAction");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new UseItemOnC2S(0, new Vector3i(1, 2, 3), 1, 0.5f, 0.25f, 0.75f, false, 9)), "round-trip UseItemOn");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new SetPlayerPositionC2S(1.5, 64.0, -3.5, true)), "round-trip SetPlayerPosition");
        Assert.True(RoundTrip(protocol, ConnectionState.Play,
            new KeepAliveC2S(123_456_789L)), "round-trip KeepAlive");
    }

    // -- Block-state + item mapping (no server needed) -----------------------
    [Fact]
    public void StateAndItemMapping() {
        Assert.True(Types.StateId(new BlockState(VanillaMod.Chest).Set(State.Facing, "east")) == 2973,
            "state: facing maps to vanilla id (chest east = 2955 + 3*6)");
        Assert.True(Types.StateId(new BlockState(VanillaMod.Wool).Set(State.Color, "red")) == 2061,
            "state: wool colour override (red = 2047 + 14)");
        Assert.True(Types.FromVanillaItem(194).State?.Get(State.Color) == 14,
            "item: vanilla wool id -> coloured stack (red 194 -> colour 14)");
        Assert.True(Types.ItemId(Types.FromVanillaItem(194)) == 194,
            "item: coloured wool stack round-trips its vanilla id (red 194)");
        Assert.True(Types.FromVanillaItem(807).Type == VanillaMod.Stick && Types.FromVanillaItem(807).Type is not BlockType,
            "item: a non-block item (stick = 807) is recovered by the reverse table, not just blocks");
        Assert.True(Types.ItemId(VanillaMod.Stick) == 807, "item: stick round-trips its vanilla id");

        var woolInv = new InventoryEntityComponent();
        woolInv.Add(Types.FromVanillaItem(194)); // red wool
        woolInv.Add(Types.FromVanillaItem(180)); // white wool
        Assert.True(woolInv.Main(0).State?.Get(State.Color) == 14 && woolInv.Main(1).State?.Get(State.Color) == 0,
            "pickup: different wool colours take separate slots");
        woolInv.Add(Types.FromVanillaItem(194)); // another red merges with the first
        Assert.True(woolInv.Main(0).Count == 2 && woolInv.Main(1).Count == 1, "pickup: same wool colour stacks");
    }

    // -- 1.19.4 (762) <-> 1.20.1 (763): same mapping logic, only the 1.20-shifted wire ids differ (inheritance delta) --
    [Fact]
    public void Je762IsJe763WithThe120IdDeltas() {
        var v762 = new TypeMapper(typeof(ProtocolJE762));
        var v763 = new TypeMapper(typeof(ProtocolJE763));

        // Content not shifted by the 1.20 additions is identical across both versions.
        foreach (var m in new TypeMapper[] { v762, v763 }) {
            Assert.Equal(1, m.StateId(VanillaMod.Stone));
            Assert.Equal(112, m.StateId(VanillaMod.Sand));
            Assert.Equal(117, m.StateId(VanillaMod.RedSand));
            Assert.Equal(43, m.ItemId(VanillaMod.Bedrock));
            Assert.Equal(54, m.EntityTypeId(CoreMod.Item)); // entity ids unchanged 762->763
        }

        // Block-states the 1.20 content shifted up (incl. the chest facing layout + wool colour override).
        Assert.Equal(2951, v762.StateId(VanillaMod.Chest));
        Assert.Equal(2955, v763.StateId(VanillaMod.Chest));
        Assert.Equal(2969, v762.StateId(new BlockState(VanillaMod.Chest).Set(State.Facing, "east"))); // 2951 + 3*6
        Assert.Equal(2973, v763.StateId(new BlockState(VanillaMod.Chest).Set(State.Facing, "east")));
        Assert.Equal(2057, v762.StateId(new BlockState(VanillaMod.Wool).Set(State.Color, "red")));     // 2043 + 14
        Assert.Equal(2061, v763.StateId(new BlockState(VanillaMod.Wool).Set(State.Color, "red")));

        // Item ids the 1.20 content shifted up.
        Assert.Equal((275, 277), (v762.ItemId(VanillaMod.Chest), v763.ItemId(VanillaMod.Chest)));
        Assert.Equal((46, 47), (v762.ItemId(VanillaMod.RedSand), v763.ItemId(VanillaMod.RedSand)));
        Assert.Equal((47, 48), (v762.ItemId(VanillaMod.Gravel), v763.ItemId(VanillaMod.Gravel)));
        Assert.Equal((803, 807), (v762.ItemId(VanillaMod.Stick), v763.ItemId(VanillaMod.Stick)));

        // The wool colour override AND its inverse (FromVanillaItem) both track the wool item base (179 vs 180).
        var redWool = new ItemStack(VanillaMod.Wool).WithState(new BlockState(VanillaMod.Wool).Set(State.Color, "red"));
        Assert.Equal(193, v762.ItemId(redWool)); // 179 + 14
        Assert.Equal(194, v763.ItemId(redWool)); // 180 + 14
        Assert.Equal(14, v762.FromVanillaItem(193).State?.Get(State.Color));
        Assert.Equal(14, v763.FromVanillaItem(194).State?.Get(State.Color));

        // Both protocols expose the same unified TypeMapper class; the version-specific ids are data, resolved
        // per protocol type (the deltas above prove 762 and 763 resolve differently from the same mapper class).
        Assert.Equal(762, new ProtocolJE762().Version);
        Assert.Equal(763, new ProtocolJE763().Version);
        Assert.IsType<TypeMapper>(new ProtocolJE762().Types);
        Assert.IsType<TypeMapper>(new ProtocolJE763().Types);
    }

    // -- The `missing` placeholder borrows stone's wire ids but must still read as custom (distinct, non-stacking) --
    [Fact]
    public void MissingBlockBorrowsStoneWireIdsButReadsCustom() {
        var mapper = new TypeMapper(typeof(ProtocolJE763));
        // Renders as stone on the wire (no native appearance of its own)...
        Assert.Equal(mapper.StateId(VanillaMod.Stone), mapper.StateId(CoreMod.Missing));
        Assert.Equal(mapper.ItemId(VanillaMod.Stone), mapper.ItemId(CoreMod.Missing));
        // ...but is flagged custom so the slot encoder gives it a distinct name + identity marker, unlike real stone.
        Assert.True(CoreMod.Missing.IsCustom);
        Assert.False(VanillaMod.Stone.IsCustom);
    }

    // -- 762->763 packet-BODY deltas (ids are identical; three bodies the 1.20 update changed) --
    [Fact]
    public void Je762PacketBodiesMatch119_4Shape() {
        var p762 = new ProtocolJE762();
        var p763 = new ProtocolJE763();

        // Join Game + Respawn: 1.20 appended a portal-cooldown VarInt(0) => the 763 body is exactly 1 byte longer.
        var join = new JoinGameS2C(1, 0, "minecraft:overworld", 0L, 8, false);
        Assert.Equal(p762.EncodePayload(join).Length + 1, p763.EncodePayload(join).Length);
        var respawn = new RespawnS2C("minecraft:overworld", "minecraft:overworld", 0L, 0, true);
        Assert.Equal(p762.EncodePayload(respawn).Length + 1, p763.EncodePayload(respawn).Length);

        // Chunk Data: 1.19.4 carries a trust-edges bool before the light section => the 762 payload is 1 byte longer.
        var world = new World("overworld", new FlatChunkGenerator());
        var c762 = ((ChunkDataS2C)p762.BuildChunk(world, 0, 0)).Payload;
        var c763 = ((ChunkDataS2C)p763.BuildChunk(world, 0, 0)).Payload;
        Assert.Equal(c763.Length + 1, c762.Length);
    }

    // -- Block breaking: bare-hand mining time from hardness --------------------------
    [Fact]
    public void BreakTicksFollowsBareHandHardness() {
        // Vanilla bare-hand: 30 ticks per hardness when hand-harvestable, 100 when a tool is required.
        Assert.True(VanillaMod.Dirt.TryGet<BreakableBlockDescriptor>(out var dirt), "dirt is breakable");
        Assert.Equal(15, dirt.BreakTicks);   // 0.5 * 30 (no tool needed to harvest)
        Assert.True(VanillaMod.Stone.TryGet<BreakableBlockDescriptor>(out var stone), "stone is breakable");
        Assert.Equal(150, stone.BreakTicks); // 1.5 * 100 (needs a pickaxe)
        Assert.False(VanillaMod.Bedrock.TryGet<BreakableBlockDescriptor>(out _), "bedrock is unbreakable");
    }

    // -- Block breaking: survival enforces the mining delay server-side ----------------
    [Fact]
    public void SurvivalDiggingEnforcesDelay() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("dig-test"), MaxPlayers = 20, TicksPerSecond = 20 }, capture);
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);

        DetachSyncContext();
        handler.Handle(client, new LoginStartC2S("Digger", Guid.Empty));
        Settle(server);
        Assert.True(server.TryGetPlayer(client.Id, out var ctx), "player resolved"); // players join in survival

        var pos = new Vector3i(0, 4, 0);
        Assert.True(server.DefaultWorld.GetBlock(pos) == VanillaMod.GrassBlock, "target is grass (hardness 0.6)");

        // Start digging: the block is NOT removed; a timed dig is recorded (0.6 * 30 = 18 ticks).
        handler.Handle(client, new PlayerActionC2S(0, pos, 1, 10));
        server.Scheduler.Run();
        Assert.True(server.DefaultWorld.GetBlock(pos) == VanillaMod.GrassBlock, "start does not break in survival");
        Assert.True(ctx.GetDigging().Active && ctx.GetDigging().RequiredTicks == 18, "dig recorded, 18 ticks required");

        // Finishing too early is rejected: the block stays and the server re-sends it so the client rolls back.
        client.Sent.Clear();
        handler.Handle(client, new PlayerActionC2S(2, pos, 1, 11));
        server.Scheduler.Run();
        Assert.True(server.DefaultWorld.GetBlock(pos) == VanillaMod.GrassBlock, "premature finish rejected");
        Assert.True(
            client.Sent.Any(m => m is BlockUpdateS2C b && b.Position == pos && b.Block == VanillaMod.GrassBlock),
            "rejected finish re-sends the still-present block");

        // Restart the dig, then simulate the required time passing (backdate the start): the finish now breaks it.
        handler.Handle(client, new PlayerActionC2S(0, pos, 1, 12));
        server.Scheduler.Run();
        ctx.GetDigging().StartTick -= 1000; // pretend well over 18 ticks elapsed
        capture.Broadcasts.Clear();
        handler.Handle(client, new PlayerActionC2S(2, pos, 1, 13));
        server.Scheduler.Run();
        Assert.True(server.DefaultWorld.GetBlock(pos).IsAir, "finish after the delay breaks the block");
        Assert.True(
            capture.Broadcasts.Any(m => m is BlockUpdateS2C b && b.Position == pos && b.Block == CoreMod.Air),
            "valid break is broadcast");
    }

    // -- Handler flow over an in-memory transport ----------------------------
    [Fact]
    public void HandlerFlow() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("self-test"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);

        // Login.
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(client.State == ConnectionState.Play, "login: switched to Play");
        Assert.True(client.Sent.Any(m => m is LoginSuccessS2C), "login: sent LoginSuccess");
        Assert.True(client.Sent.Any(m => m is JoinGameS2C), "login: sent JoinGame");
        Assert.True(client.Sent.Any(m => m is SetCenterChunkS2C), "login: sent Set Center Chunk");
        Assert.True(client.Sent.OfType<ChunkDataS2C>().Any(), "login: streamed chunk data");
        Assert.True(client.Sent.Any(m => m is SetHealthS2C), "login: sent SetHealth");
        Assert.True(server.PlayerCount == 1, "login: player spawned");
        Assert.True(
            ServerPacketHandler.OfflineUuid("Steve") == ServerPacketHandler.OfflineUuid("Steve") &&
            ServerPacketHandler.OfflineUuid("Steve") != Guid.Empty,
            "login: offline UUID is deterministic + non-empty");

        // Digging (creative instant break, status 0).
        client.Sent.Clear();
        capture.Broadcasts.Clear();
        Assert.True(server.TryGetPlayer(client.Id, out var digger), "dig: player resolved");
        digger.GetPlayer().GameMode = CoreMod.Creative; // creative instant-breaks on the start action
        var digPos = new Vector3i(0, 4, 0);
        Assert.True(server.DefaultWorld.GetBlock(digPos) == VanillaMod.GrassBlock, "dig: target starts as grass");
        handler.Handle(client, new PlayerActionC2S(0, digPos, 1, 42));
        server.Scheduler.Run(); // dig is deferred to the tick's single-writer phase
        Assert.True(server.DefaultWorld.GetBlock(digPos).IsAir, "dig: block removed");
        Assert.True(client.Sent.Any(m => m is AckBlockChangeS2C a && a.Sequence == 42), "dig: acknowledged sequence");
        Assert.True(
            capture.Broadcasts.Any(m => m is BlockUpdateS2C b && b.Position == digPos && b.Block == CoreMod.Air),
            "dig: BlockUpdate broadcast");
        server.AnnounceSystems(); // assign the drop a net id
        server.FlushSystems();    // the tracker spawns it to the in-view player
        Assert.True(client.Sent.Any(m => m is SpawnEntityS2C), "dig: drop spawned to the player (SpawnEntity)");

        // Placement (held item is Stone; placing on the top face of a grass block).
        capture.Broadcasts.Clear();
        var placeOn = new Vector3i(2, 4, 2);
        var placedAt = new Vector3i(2, 5, 2);
        handler.Handle(client, new UseItemOnC2S(0, placeOn, (int)BlockFace.Top, 0.5f, 1f, 0.5f, false, 43));
        server.Scheduler.Run(); // placement is deferred to the tick's single-writer phase
        Assert.True(server.DefaultWorld.GetBlock(placedAt) == VanillaMod.Stone, "place: stone placed above clicked face");
        Assert.True(
            capture.Broadcasts.Any(m => m is BlockUpdateS2C b && b.Position == placedAt && b.Block == VanillaMod.Stone),
            "place: BlockUpdate broadcast");

        // -- Containers: open a chest, move an item in, sync to a second viewer --
        client.Sent.Clear();
        var chestEntity = new BlockEntity(server.DefaultWorld, new Vector3i(10, 5, 10), VanillaMod.Chest);
        server.DefaultWorld.SetBlockEntity(chestEntity);
        server.Containers.Open(client.Id, chestEntity);
        Assert.True(client.Sent.OfType<OpenScreenS2C>().Any(), "container: open sent OpenScreen");
        Assert.True(client.Sent.OfType<SetContainerContentS2C>().Any(), "container: open sent Content");

        int win = client.Sent.OfType<OpenScreenS2C>().First().WindowId;
        // Pick up the hotbar stone (chest-window slot 54 = Main(0)) then drop it into chest slot 0.
        server.Containers.OnClick(client.Id, new ClickContainerC2S(win, 0, 54, 0, 0));
        server.Containers.OnClick(client.Id, new ClickContainerC2S(win, 1, 0, 0, 0));
        var chestInv = chestEntity.Get<InventoryComponent>();
        Assert.True(!chestInv[0].IsEmpty && chestInv[0].Type == VanillaMod.Stone, "container: stone moved into chest");

        // A second viewer of the same chest is synced when the first viewer clicks.
        var viewer = new CaptureNetClient(2, protocol) { State = ConnectionState.Play };
        capture.Register(viewer);
        server.AddPlayer(viewer, "Steve2", ServerPacketHandler.OfflineUuid("Steve2"));
        server.Containers.Open(viewer.Id, chestEntity);
        viewer.Sent.Clear();
        server.Containers.OnClick(client.Id, new ClickContainerC2S(win, 2, 0, 0, 0)); // #1 picks the stack back up
        Assert.True(viewer.Sent.OfType<SetContainerContentS2C>().Any(), "container: second viewer synced");
        server.RemovePlayer(viewer.Id);

        // -- Item pickup: a dropped stack near the player is collected via collision --
        var dropEntity = server.DefaultWorld.SpawnDroppedItem(new Vector3i(0, 5, 0), new ItemStack(VanillaMod.Cobblestone, 1));
        server.DefaultWorld.Ecs.Get<VelocityEntityComponent>(dropEntity) = new VelocityEntityComponent(0, 0, 0); // pin it under the player (no random scatter)
        server.AnnounceSystems();                    // assign its network id (pickup ignores un-announced drops)
        for (int i = 0; i < 12; i++) server.DefaultWorld.Tick(); // age past pickup delay, settle, ItemPickupSystem collects it
        server.FlushSystems();                                   // project the pickup (collect animation + removal)
        Assert.True(!server.DefaultWorld.Ecs.IsAlive(dropEntity), "pickup: drop entity removed");
        Assert.True(server.TryGetPlayer(client.Id, out var context), "player is present"); // TODO: change name
        var pickInv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        bool hasCobble = false;
        for (int s = 0; s < InventoryEntityComponent.MainSize; s++)
            if (pickInv.Main(s).Type == VanillaMod.Cobblestone) hasCobble = true;
        Assert.True(hasCobble, "pickup: item added to inventory");
        int pickerNetId = server.DefaultWorld.Ecs.Get<PlayerEntityComponent>(context.Entity).NetId;
        Assert.True(
            capture.Broadcasts.Any(m => m is CollectItemS2C c && c.CollectorEntityId == pickerNetId && c.PickupItemCount == 1),
            "pickup: collect-item animation broadcast (collector + count)");

        // -- Block state: set + read a chest's facing; break clears it --
        var statePos = new Vector3i(7, 5, 7);
        server.DefaultWorld.SetBlock(statePos, VanillaMod.Chest);
        server.DefaultWorld.SetBlockState(statePos, new BlockState(VanillaMod.Chest).Set(State.Facing, "east"));
        Assert.True(
            server.DefaultWorld.GetBlockState(statePos)?.Get(State.Facing) == State.Facing.IndexOf("east"),
            "state: facing stored + read back");
        server.DefaultWorld.BreakBlock(statePos);
        Assert.True(server.DefaultWorld.GetBlockState(statePos) is null, "state: cleared on break");

        // Disconnect cleanup.
        server.RemovePlayer(client.Id);
        Assert.True(server.PlayerCount == 0, "disconnect: player despawned");
    }

    // -- Drops: a self-dropping stateful block drops an ItemStack carrying its state --
    [Fact]
    public void WoolDropsAsColouredStack() {
        var world = new World("drop", new FlatChunkGenerator());
        var pos = new Vector3i(3, 6, 3);
        world.SetBlock(pos, VanillaMod.Wool);
        world.SetBlockState(pos, new BlockState(VanillaMod.Wool).Set(State.Color, "red"));

        world.BreakBlock(pos);

        ItemStack? dropped = null;
        world.Ecs.Query(in new QueryDescription().WithAll<PickupEntityComponent>(),
            (ref PickupEntityComponent d) => dropped = d.Stack);
        Assert.True(dropped is { } ds && ds.Type == VanillaMod.Wool && ds.State?.Get(State.Color) == 14,
            "drop: wool drops as a coloured ItemStack");
    }

    // -- Drops: the item's fresh pop velocity is delivered via Set Entity Velocity (announced pre-physics) --
    [Fact]
    public void DroppedItemSpawnCarriesPopVelocity() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);

        // A viewer near the drop: entity visibility is per-player now, so the tracker sends the spawn to it.
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Viewer", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        server.Scheduler.Run();
        client.Sent.Clear();

        // Break a block -> drop spawns with the upward pop (DropVelocity Y = 0.2). Flush BEFORE any physics tick,
        // so the velocity the tracker sends is the un-decayed spawn value.
        server.DefaultWorld.BreakBlock(new Vector3i(0, 4, 0));
        server.AnnounceSystems(); // assign the drop a net id
        server.FlushSystems();    // the tracker spawns it to the in-view player

        // Velocity is delivered by the explicit Set Entity Velocity packet (the 1.20.1 client ignores
        // the spawn-packet velocity for items); the spawn packet's velocity is deliberately zeroed so it
        // can't double-apply. 0.2 blocks/tick x 8000 = 1600.
        var spawn = client.Sent.OfType<SpawnEntityS2C>().First();
        Assert.Equal(0, spawn.VelocityY);
        var vel = client.Sent.OfType<SetEntityVelocityS2C>().First();
        Assert.Equal(spawn.EntityId, vel.EntityId);
        Assert.True(vel.VelocityY > 1000, $"item pop velocity sent (VelocityY={vel.VelocityY})");
    }

    // -- Falling blocks: sand over air detaches into a falling_block entity, falls, and re-places --
    [Fact]
    public void SandFallsAndRePlacesOnLanding() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var world = server.DefaultWorld;

        // A viewer (spawn column 0,0): the sand column (1,1) is within view, so the tracker spawns/despawns it for it.
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Viewer", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        server.Scheduler.Run();

        // A lone stone platform high above the flat terrain, with sand a few cells above it over air.
        var floor = new Vector3i(20, 100, 20);
        var sand = new Vector3i(20, 104, 20);
        var landed = new Vector3i(20, 101, 20); // rests on top of the floor
        world.SetBlock(floor, VanillaMod.Stone);
        world.SetBlock(sand, VanillaMod.Sand);

        var fallingQuery = new QueryDescription().WithAll<FallingBlockEntityComponent>();

        // Air beneath the sand -> it detaches: the source cell clears and one falling entity appears. The tracker
        // stamps its net id and spawns it to the in-view viewer synchronously on spawn, so clear FIRST.
        client.Sent.Clear();
        SharpMinerals.Level.Systems.FallingBlockSystem.TryStartFalling(server, world, sand);
        Assert.True(world.GetBlock(sand).IsAir, "fall: the sand's source cell is cleared");
        Assert.Equal(1, world.Ecs.CountEntities(in fallingQuery));

        var spawn = client.Sent.OfType<SpawnEntityS2C>().First();
        Assert.Equal(CoreMod.FallingBlock, spawn.Type);
        Assert.Equal<BlockType>(VanillaMod.Sand, spawn.BlockData);

        // Landing happens inside the world tick (FallingBlockSystem fires the block's IOnLand reaction and
        // despawns the entity, recording the landing); its Flush projects the block update and the tracker the removal.
        for (int i = 0; i < 200 && world.Ecs.CountEntities(in fallingQuery) != 0; i++)
            world.Tick();
        server.FlushSystems();

        Assert.Equal(0, world.Ecs.CountEntities(in fallingQuery));
        Assert.Equal(VanillaMod.Sand, world.GetBlock(landed));
        Assert.True(world.GetBlock(sand).IsAir, "fall: the original cell stays air after landing");
        Assert.Contains(capture.Broadcasts.OfType<BlockUpdateS2C>(),
            b => b.Position.Equals(landed) && b.Block == VanillaMod.Sand);
        Assert.Contains(client.Sent.OfType<RemoveEntitiesS2C>(), r => r.EntityIds.Contains(spawn.EntityId));
    }

    // -- A 1.20.1 client renders a player only if its tab-list entry (PlayerInfoUpdate) arrives BEFORE the spawn.
    //    Capture the whole join sequence (do NOT clear) and assert both the entry is sent and it precedes the spawn. --
    [Fact]
    public void PlayerInfoEntryIsSentBeforeSpawn() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var handler = new ServerPacketHandler(server);

        void FullTick() {
            server.Scheduler.Run();
            server.AnnounceSystems();
            foreach (var w in worlds.Values) w.Tick();
            server.FlushSystems();
            server.Scheduler.Run();
        }

        // Alice is established first.
        var alice = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(alice);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(alice, new LoginStartC2S("Alice", Guid.NewGuid()));
        Settle(server); // drive the join-triggered async column streaming to completion
        for (int i = 0; i < 3; i++) FullTick();

        // Bob joins; capture EVERYTHING Alice and Bob receive from here on (no clear).
        alice.Sent.Clear();
        var bob = new CaptureNetClient(2, protocol) { State = ConnectionState.Login };
        capture.Register(bob);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(bob, new LoginStartC2S("Bob", Guid.NewGuid()));
        Settle(server); // drive the join-triggered async column streaming to completion
        for (int i = 0; i < 3; i++) FullTick();

        Assert.True(server.TryGetPlayer(1, out var pa));
        Assert.True(server.TryGetPlayer(2, out var pb));
        var aInfo = server.DefaultWorld.Ecs.Get<PlayerEntityComponent>(pa.Entity);
        var bInfo = server.DefaultWorld.Ecs.Get<PlayerEntityComponent>(pb.Entity);

        // Alice must learn Bob's profile, and it must arrive BEFORE Bob's entity spawn, or her client drops the spawn.
        int aliceProfileOfBob = alice.Sent.FindIndex(m => m is PlayerInfoUpdateS2C u && u.Entries.Any(e => e.Uuid == bInfo.Uuid));
        int aliceSpawnOfBob = alice.Sent.FindIndex(m => m is SpawnPlayerS2C s && s.EntityId == bInfo.NetId);
        Assert.True(aliceProfileOfBob >= 0, "Alice received Bob's PlayerInfoUpdate");
        Assert.True(aliceSpawnOfBob >= 0, "Alice received Bob's SpawnPlayer");
        Assert.True(aliceProfileOfBob < aliceSpawnOfBob, "Bob's profile precedes his spawn for Alice");

        // And symmetrically Bob must learn Alice's profile before her spawn.
        int bobProfileOfAlice = bob.Sent.FindIndex(m => m is PlayerInfoUpdateS2C u && u.Entries.Any(e => e.Uuid == aInfo.Uuid));
        int bobSpawnOfAlice = bob.Sent.FindIndex(m => m is SpawnPlayerS2C s && s.EntityId == aInfo.NetId);
        Assert.True(bobProfileOfAlice >= 0, "Bob received Alice's PlayerInfoUpdate");
        Assert.True(bobSpawnOfAlice >= 0, "Bob received Alice's SpawnPlayer");
        Assert.True(bobProfileOfAlice < bobSpawnOfAlice, "Alice's profile precedes her spawn for Bob");
    }

    // -- A late joiner and an established, moved player still spawn to each other via the tracker --
    [Fact]
    public void LateJoinerSeesEstablishedPlayer() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var handler = new ServerPacketHandler(server);
        var world = server.DefaultWorld;

        void FullTick() {
            server.Scheduler.Run();
            server.AnnounceSystems();
            foreach (var w in worlds.Values) w.Tick();
            server.FlushSystems();
            server.Scheduler.Run();
        }

        // Alice joins and the server runs a few ticks (she is "established").
        var c1 = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(c1);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(c1, new LoginStartC2S("Alice", Guid.NewGuid()));
        Settle(server); // drive the join-triggered async column streaming to completion
        // Confirm the join teleport, else MovePlayer ignores her position packets as stale.
        var sync1 = c1.Sent.OfType<SynchronizePlayerPositionS2C>().First();
        handler.Handle(c1, new ConfirmTeleportationC2S(sync1.TeleportId));
        for (int i = 0; i < 5; i++) FullTick();

        // Alice moves a couple of blocks (same chunk column) - this drives PlayerMovementSystem + Restream.
        handler.Handle(c1, new SetPlayerPositionAndRotationC2S(3.5, WorldDefaults.SurfaceY, 2.5, 90f, 0f, true));
        for (int i = 0; i < 3; i++) FullTick();

        // Bob joins later. The mutual spawns now happen synchronously during his join, so clear Alice's buffer
        // FIRST and capture from his join onward (don't clear afterwards).
        c1.Sent.Clear();
        var c2 = new CaptureNetClient(2, protocol) { State = ConnectionState.Login };
        capture.Register(c2);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(c2, new LoginStartC2S("Bob", Guid.NewGuid()));
        Settle(server); // drive the join-triggered async column streaming to completion
        for (int i = 0; i < 3; i++) FullTick();

        Assert.True(server.TryGetPlayer(1, out var ctx1));
        Assert.True(server.TryGetPlayer(2, out var ctx2));
        int eid1 = world.Ecs.Get<PlayerEntityComponent>(ctx1.Entity).NetId;
        int eid2 = world.Ecs.Get<PlayerEntityComponent>(ctx2.Entity).NetId;

        var bobSeesAlice = c2.Sent.OfType<SpawnPlayerS2C>().Where(s => s.EntityId == eid1).ToList();
        var aliceSeesBob = c1.Sent.OfType<SpawnPlayerS2C>().Where(s => s.EntityId == eid2).ToList();
        Assert.NotEmpty(bobSeesAlice);  // Bob sees established Alice
        Assert.NotEmpty(aliceSeesBob);  // Alice sees Bob
        // Alice's spawn position must carry her moved-to location, not the (0,0,0) blueprint default.
        Assert.True(bobSeesAlice[0].X == 3.5 && bobSeesAlice[0].Z == 2.5, $"Alice spawned at ({bobSeesAlice[0].X},{bobSeesAlice[0].Z})");

        // Now run many more ticks with both standing still: neither may be erroneously removed (a spawn-then-remove
        // flicker would leave the client with no player rendered).
        c1.Sent.Clear();
        c2.Sent.Clear();
        for (int i = 0; i < 20; i++) FullTick();
        Assert.DoesNotContain(c2.Sent.OfType<RemoveEntitiesS2C>(), r => r.EntityIds.Contains(eid1));
        Assert.DoesNotContain(c1.Sent.OfType<RemoveEntitiesS2C>(), r => r.EntityIds.Contains(eid2));
        // And both remain recorded as spawned in each viewer's tracker.
        Assert.Contains(eid1, world.Ecs.Get<EntityTrackerComponent>(ctx2.Entity).Sent);
        Assert.Contains(eid2, world.Ecs.Get<EntityTrackerComponent>(ctx1.Entity).Sent);
    }

    // -- Mods: the ModLoader discovers a compiled-in mod and its OnServerStarted registers a command --
    [Fact]
    public void ModLoaderLoadsTestModAndRegistersItsCommand() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);

        // Load the test-harness mod. Under AOT (no reflection mod-discovery) use the type-safe TryLoad<T> - the same
        // compiled-in path the CLI uses; otherwise exercise the reflection-based LoadFrom (the dynamic-discovery path).
        var loader = new ModLoader();
#if AOT
        loader.TryLoad(new SharpMinerals.TestMod.TestMod());
#else
        loader.LoadFrom(typeof(SharpMinerals.TestMod.TestMod).Assembly);
#endif
        Assert.Single(loader.Mods);
        Assert.Equal("sharpminerals_test", loader.Mods[0].Info.ModId);

        // OnServerStarted registers /test on the dispatcher; running it (no @id) broadcasts to play clients.
        loader.StartAll(server);
        server.Sender.RunCommand("test hello world");
        server.Scheduler.Run(); // RunCommand -> ExecuteAsync defers brig.Execute to the scheduler
        Assert.Contains(capture.Broadcasts.OfType<TestCommandS2C>(), m => m.Command == "hello world");
    }

    sealed class VersionProbeMod : Mod { }

    // -- A mod is loaded only if its declared target server version is compatible (and its own is valid) --
    [Fact]
    public void ModLoaderGatesOnTargetServerVersion() {
        var loader = new ModLoader { ServerVersion = SemanticVersion.Parse("0.1.0") };

        Assert.True(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("compat", "1.0.0") { TargetServerVersion = "0.1.0" }));
        Assert.False(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("too_new", "1.0.0") { TargetServerVersion = "0.2.0" }));  // needs newer server
        Assert.False(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("wrong_major", "1.0.0") { TargetServerVersion = "1.0.0" })); // major mismatch
        Assert.False(loader.TryLoad(new VersionProbeMod(), new ModInfoAttribute("bad_version", "not-semver")));                            // invalid own version

        Assert.Single(loader.Mods);                                     // only the compatible mod loaded
        Assert.Equal("compat", loader.Mods[0].Info.ModId);
        Assert.Equal(SemanticVersion.Parse("1.0.0"), loader.Mods[0].Version); // parsed version exposed on the mod
    }

    // -- Custom objects: a mod-added type renders as the fallback item but carries differentiating NBT --
    [Fact]
    public void CustomItemCarriesNameAndIdentityNbt() {
        // A minecraft-namespaced item is native by default; .Custom(true) overrides that (a server item with no
        // vanilla equivalent). Real mods get custom for free (non-minecraft namespace) - see the gem test below.
        var custom = ItemType.Register("sm_custom_test").Custom(true);
        var mapper = new TypeMapper(typeof(ProtocolJE763));
        Assert.True(custom.IsCustom);
        Assert.False(VanillaMod.Stone.IsCustom);

        byte[] Encode(ItemStack stack) {
            using var ms = new System.IO.MemoryStream();
            var s = new MinecraftStream(ms, leaveOpen: true) { Types = mapper };
            SlotWire.WriteStack(s, stack);
            return ms.ToArray();
        }

        var customText = System.Text.Encoding.UTF8.GetString(Encode(new ItemStack(custom)));
        var stoneText = System.Text.Encoding.UTF8.GetString(Encode(new ItemStack(VanillaMod.Stone)));

        // The custom item gets a translatable display name keyed by its namespaced id (item.<namespace>.<path>,
        // lang-file ready), with a humanised fallback, and an identity marker that keeps the client from stacking
        // it with the fallback item or other custom types. Registered outside a mod, so the namespace is minecraft.
        Assert.Contains("item.minecraft.sm_custom_test", customText);
        Assert.Contains("Sm Custom Test", customText);
        Assert.Contains("SharpMineralsType", customText);
        // A vanilla item is a plain slot - no display name, no marker (so it stacks normally).
        Assert.DoesNotContain("SharpMineralsType", stoneText);
    }

    // -- Custom objects: the identity survives the wire round-trip when the client echoes the slot back --
    [Fact]
    public void CustomItemIdentitySurvivesSlotRoundTrip() {
        var custom = ItemType.Register("sm_roundtrip_item").Custom(true);
        var mapper = new TypeMapper(typeof(ProtocolJE763));

        using var ms = new System.IO.MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true) { Types = mapper };
        SlotWire.WriteStack(w, new ItemStack(custom, 3)); // server -> client: fallback id + count + NBT marker

        ms.Position = 0;
        var r = new MinecraftStream(ms, leaveOpen: true) { Types = mapper };
        var restored = SlotWire.ReadStack(r); // client echo decoded back to our internal ItemStack
        Assert.NotNull(restored);
        Assert.Equal(custom, restored!.Value.Type); // recovered the custom type, not the fallback (stone)
        Assert.Equal(3, restored.Value.Count);
    }

    // -- Namespaced identifiers: minecraft default, mod namespace, lookup normalization, persistence --
    [Fact]
    public void ItemBlockNamespacesResolveAndPersist() {
        // Built-ins live under the minecraft namespace; Id.Name is the path, Id.ToString() the full namespace:path.
        Assert.Equal("stone", VanillaMod.Stone.Id.Name);
        Assert.Equal("minecraft", VanillaMod.Stone.Id.Namespace);
        Assert.Equal("minecraft:stone", VanillaMod.Stone.Id.Full);
        Assert.Equal(new Identifier("minecraft", "stone"), VanillaMod.Stone.Id); // value equality

        // A bare path defaults to minecraft; the qualified form resolves to the same instance.
        Assert.Same(VanillaMod.Stone, TestContent.FindItem("stone"));
        Assert.Same(VanillaMod.Stone, TestContent.FindItem("minecraft:stone"));
        Assert.Null(TestContent.FindItem("nope:stone"));

        // Mod content is namespaced under the loading mod's id (ModLoader sets CurrentNamespace around OnInitialize).
        ModContent.CurrentNamespace = "ns_test_mod";
        var gem = ItemType.Register("gem");
        ModContent.CurrentNamespace = "minecraft";
        Assert.Equal("ns_test_mod:gem", gem.Id.Full);
        Assert.Same(gem, TestContent.FindItem("ns_test_mod:gem"));
        Assert.True(gem.IsCustom); // modded -> not vanilla -> falls back on the wire

        // Persistence writes the full namespaced id and round-trips it (portable, collision-free).
        using var ms = new System.IO.MemoryStream();
        StackCodec.Write(new MinecraftStream(ms, leaveOpen: true), new ItemStack(gem, 5));
        ms.Position = 0;
        var back = StackCodec.Read(new MinecraftStream(ms, leaveOpen: true));
        Assert.Equal(gem, back.Type);
        Assert.Equal(5, back.Count);
    }

    // -- A mod's wire mapping: VanillaMapping is the registration point; the mapper honours it (no hardcoded switch) --
    [Fact]
    public void ModdedTypeWithVanillaMappingResolvesToMappedWireIds() {
        ModContent.CurrentNamespace = "ns_map_test";
        // Map to dirt (state 10 / item 15) - distinct from the stone fallback (1), so honouring is observable.
        var moddedBlock = BlockType.Register("ruby_block").Add(new VanillaMapping(VanillaMod.Dirt));
        var copied = BlockType.Register("ruby_ore").CopyMapping(moddedBlock); // borrows the same mapping
        var moddedEntity = EntityType.Register("spark").Add(new VanillaMapping(CoreMod.Item));
        var unmapped = BlockType.Register("void_block"); // no mapping -> stone fallback
        ModContent.CurrentNamespace = "minecraft";

        // Constructed after registration so the precomputed block-state table includes the new blocks.
        var mapper = new TypeMapper(typeof(ProtocolJE763));

        // Mapped modded block takes dirt's wire ids, not the stone fallback; CopyMapping carries it across.
        Assert.Equal(10, mapper.StateId(moddedBlock));
        Assert.Equal(15, mapper.ItemId(moddedBlock));
        Assert.Equal(10, mapper.StateId(copied));
        Assert.Equal(15, mapper.ItemId(copied));

        // Mapped modded entity resolves through the entity table (replacing the old hardcoded switch).
        Assert.Equal(54, mapper.EntityTypeId(moddedEntity));

        // Unmapped modded content still falls back to stone and keeps the custom marker (distinct identity).
        Assert.Equal(1, mapper.StateId(unmapped));
        Assert.Equal(1, mapper.ItemId(unmapped));
        Assert.True(unmapped.IsCustom);
        Assert.True(moddedBlock.IsCustom);

        // An unmapped modded entity has no 1.20.1 wire id and can't be spawned to a client.
        var ghost = EntityType.Register("ghost");
        Assert.Throws<ArgumentOutOfRangeException>(() => mapper.EntityTypeId(ghost));
    }

    // -- Red sand: built with the component Copy API - borrows Sand's falling behaviour, drops itself --
    [Fact]
    public void RedSandFallsLikeSandButDropsItself() {
        // The falling behaviour was copied from Sand (.Copy<FallingBlockDescriptor>), so it's a falling block...
        Assert.True(VanillaMod.RedSand.Has<FallingBlockDescriptor>());
        Assert.True(VanillaMod.Sand.Has<FallingBlockDescriptor>());

        // ...but its drop is its own, not Sand's (Sand's DropBlockDescriptor was intentionally NOT copied).
        Assert.Equal<ItemType>(VanillaMod.RedSand, VanillaMod.RedSand.Drop?.Type);
        Assert.Equal<ItemType>(VanillaMod.Sand, VanillaMod.Sand.Drop?.Type);

        // Resolves to its real 1.20.1 wire ids (block-state 117, item 47), not the stone fallback.
        var mapper = new TypeMapper(typeof(ProtocolJE763));
        Assert.Equal(117, mapper.StateId(VanillaMod.RedSand));
        Assert.Equal(47, mapper.ItemId(VanillaMod.RedSand));
        Assert.Same(VanillaMod.RedSand, TestContent.FindItem("red_sand"));
    }

    // -- Creative: an item this server can't represent is reported + corrected, honouring the cursor --
    [Fact]
    public void CreativeSlotWithUnknownItemWarnsAndCorrectsClient() {
        var protocol = new ProtocolJE763();

        // Decode side: a present wire slot whose id maps to no SharpMinerals type reads back as null.
        using (var ms = new System.IO.MemoryStream()) {
            var w = new MinecraftStream(ms, leaveOpen: true) { Types = protocol.Types };
            w.WriteBool(true); w.WriteVarInt(99999); w.WriteByte2(1); w.WriteUByte(0x00); // present, unknown id (beyond any vanilla item), no NBT
            ms.Position = 0;
            var r = new MinecraftStream(ms, leaveOpen: true) { Types = protocol.Types };
            Assert.Null(SlotWire.ReadStack(r)); // unrepresentable -> null
        }

        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(client.Id, out var context));
        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);

        // Swap onto a FILLED slot: the invalid item is rejected, but the stone the player grabbed in the same
        // action is kept on the cursor (slot emptied), so they don't lose it. Window slot 36 = hotbar 0.
        inv.Main(0) = new ItemStack(VanillaMod.Stone, 5);
        client.Sent.Clear();
        handler.Handle(client, new SetCreativeModeSlotC2S(36, null));
        server.Scheduler.Run(); // creative slot is deferred to the tick

        Assert.Contains(client.Sent, m => m is SystemChatMessageS2C s && s.Overlay); // overlay warning forwarded
        var resync = client.Sent.OfType<SetContainerContentS2C>().Last();
        Assert.Equal<ItemType>(VanillaMod.Stone, resync.Carried.Type); // the grabbed item stays on the cursor
        Assert.True(resync.Slots[36].IsEmpty);                  // ...and the slot it came from is now empty
        Assert.True(inv.Main(0).IsEmpty);                       // server agrees: the slot was emptied (item on cursor)

        // Into an EMPTY slot: just revert that slot, WITHOUT touching the cursor (a duplicate of the swap
        // above must not wipe the grabbed item) - a single-slot correction, no content/cursor resync.
        Assert.True(inv.Main(2).IsEmpty); // hotbar 2 starts empty (player spawns with items only in 0-1)
        client.Sent.Clear();
        handler.Handle(client, new SetCreativeModeSlotC2S(38, null)); // window slot 38 = hotbar 2 (empty)
        server.Scheduler.Run();
        var slotFix = client.Sent.OfType<SetContainerSlotS2C>().Single();
        Assert.Equal(38, slotFix.Slot);
        Assert.True(slotFix.Data.IsEmpty);
        Assert.DoesNotContain(client.Sent, m => m is SetContainerContentS2C); // cursor left alone
    }

    // -- Regression: a creative Set-Creative-Slot decodes through the protocol (the type mapper must be --
    // available on the DECODE stream, not just encode - else ReadStack NRE'd on every creative add/clone). --
    [Fact]
    public void CreativeSlotPacketDecodesThroughProtocol() {
        var protocol = new ProtocolJE763();

        // The wire body a creative client sends: packet id + slot(short) + Slot.
        using var bodyMs = new System.IO.MemoryStream();
        var bw = new MinecraftStream(bodyMs, leaveOpen: true) { Types = protocol.Types };
        bw.WriteVarInt(0x2B); // Sb.SetCreativeModeSlot
        bw.WriteShort(36);
        SlotWire.WriteStack(bw, new ItemStack(VanillaMod.Stone, 5));
        var body = bodyMs.ToArray();

        // Frame it (VarInt length + body) and decode it through the protocol - the production path whose
        // stream had no Types on decode, so a creative add/clone crashed with NRE in SlotWire.ReadStack.
        using var frameMs = new System.IO.MemoryStream();
        var fw = new MinecraftStream(frameMs, leaveOpen: true);
        fw.WriteVarInt(body.Length);
        fw.Write(body, 0, body.Length);
        frameMs.Position = 0;

        var msg = protocol.ReadMessage(new MinecraftStream(frameMs, leaveOpen: true),
            ConnectionState.Play, PacketDirection.Serverbound);
        var creative = Assert.IsType<SetCreativeModeSlotC2S>(msg);
        Assert.Equal(36, creative.Slot);
        Assert.NotNull(creative.Stack);
        Assert.Equal<ItemType>(VanillaMod.Stone, creative.Stack!.Value.Type);
        Assert.Equal(5, creative.Stack.Value.Count);
    }

    // -- Chunk streaming includes block entities (a chest renders on load, not only after an update) --
    [Fact]
    public void ChunkPacketIncludesBlockEntities() {
        var world = new World("be", new FlatChunkGenerator());
        var pos = new Vector3i(3, 70, 5);
        // Just the block - NO BlockEntity instance (an unopened chest). The packet entry is derived from the
        // block state, so it must still be sent (this is exactly the "some chests don't render" case).
        world.SetBlock(pos, VanillaMod.Chest);

        var packet = ChunkSerializer.Build(Types, world, 0, 0);
        var s = new MinecraftStream(new System.IO.MemoryStream(packet.Payload, writable: false));
        s.ReadInt(); s.ReadInt();                                  // chunkX, chunkZ
        SharpMinerals.Network.Nbt.NbtReader.ReadItemNbt(s);        // consume the heightmaps NBT
        s.ReadBytes(s.ReadVarInt());                               // skip the sections blob

        Assert.Equal(1, s.ReadVarInt());                          // one block entity in the column
        Assert.Equal((3 << 4) | 5, s.ReadUByte());                // packed local XZ
        Assert.Equal((short)70, s.ReadShort());                   // world Y
        Assert.Equal(1, s.ReadVarInt());                          // minecraft:chest block-entity-type id
    }

    // -- Tab-list header/footer (0x65) encodes as two JSON components and honours the audience predicate --
    [Fact]
    public void TabListHeaderFooterEncodesAndRespectsAudience() {
        var protocol = new ProtocolJE763();
        var header = new TextComponent("Top").SetColor(TextColor.Gold);
        var footer = new TextComponent("Bottom");

        // Wire: packet id 0x65, then the header and footer as JSON chat strings.
        var bytes = protocol.EncodePayload(new PlayerListHeaderFooterS2C(header, footer));
        var s = new MinecraftStream(new System.IO.MemoryStream(bytes, writable: false));
        Assert.Equal(0x65, s.ReadVarInt());
        Assert.Equal(header.ToString(), s.ReadString());
        Assert.Equal(footer.ToString(), s.ReadString());

        // The server method sends only to the clients the predicate selects.
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 8, TicksPerSecond = 20,
        }, capture);
        var c1 = new CaptureNetClient(1, protocol) { State = ConnectionState.Play };
        var c2 = new CaptureNetClient(2, protocol) { State = ConnectionState.Play };
        capture.Register(c1);
        capture.Register(c2);

        server.SetTabListHeaderFooter(header, footer, c => c.Id == 1);
        Assert.Single(c1.Sent.OfType<PlayerListHeaderFooterS2C>());
        Assert.Empty(c2.Sent.OfType<PlayerListHeaderFooterS2C>());
    }

    // -- /clear empties the player's inventory and resyncs the window --
    [Fact]
    public void ClearCommandEmptiesInventoryAndResyncs() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        server.CommandDispatcher.RegisterClear();
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(client.Id, out var context));

        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        inv.Main(0) = new ItemStack(VanillaMod.Stone, 10);
        inv.Main(5) = new ItemStack(VanillaMod.Dirt, 3);
        inv.Offhand = new ItemStack(VanillaMod.Cobblestone, 1);
        client.Sent.Clear();

        var sender = server.DefaultWorld.Ecs.Get<SenderEntityComponent>(context.Entity);
        _ = server.CommandDispatcher.ExecuteAsync(sender, "clear", client); // synchronous-bodied
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler

        Assert.True(inv.Main(0).IsEmpty && inv.Main(5).IsEmpty && inv.Offhand.IsEmpty, "every slot cleared");
        // The emptied window is pushed back to the client so its view clears.
        var resync = client.Sent.OfType<SetContainerContentS2C>().Last();
        Assert.All(resync.Slots, s => Assert.True(s.IsEmpty));
        Assert.True(resync.Carried.IsEmpty);
    }

    // -- /give adds an item (by registry name) to the player's inventory and resyncs the window --
    [Fact]
    public void GiveCommandAddsItemAndResyncs() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        server.CommandDispatcher.RegisterGive();
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(client.Id, out var context));
        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        var sender = server.DefaultWorld.Ecs.Get<SenderEntityComponent>(context.Entity);
        client.Sent.Clear();

        // Give takes a NAMESPACED id (Brigadier's word() chokes on the ':', so the item arg is a resource-location
        // type). A namespace is required - bare names only power tab-suggestions, since several namespaces may
        // register the same path.
        _ = server.CommandDispatcher.ExecuteAsync(sender, "give minecraft:cobblestone 10", client);
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler

        int total = 0;
        for (int i = 0; i < InventoryEntityComponent.MainSize; i++)
            if (inv.Main(i).Type == VanillaMod.Cobblestone) total += inv.Main(i).Count;
        Assert.Equal(10, total);                                              // the 10 cobblestone landed in the inventory
        Assert.NotEmpty(client.Sent.OfType<SetContainerContentS2C>());        // window resynced to the client

        client.Sent.Clear();
        _ = server.CommandDispatcher.ExecuteAsync(sender, "give minecraft:wool 5", client);
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler
        int wool = 0;
        for (int i = 0; i < InventoryEntityComponent.MainSize; i++)
            if (inv.Main(i).Type == VanillaMod.Wool) wool += inv.Main(i).Count;
        Assert.Equal(5, wool);

        // A bare (namespace-less) name is rejected even though it names a real item - a namespace is required.
        client.Sent.Clear();
        _ = server.CommandDispatcher.ExecuteAsync(sender, "give cobblestone", client);
        server.Scheduler.Run();
        Assert.Empty(client.Sent.OfType<SetContainerContentS2C>());

        // An unknown (but namespaced) item is likewise rejected without touching the inventory.
        client.Sent.Clear();
        _ = server.CommandDispatcher.ExecuteAsync(sender, "give minecraft:not_a_real_item", client);
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler
        Assert.Empty(client.Sent.OfType<SetContainerContentS2C>());
    }

    // -- /summon spawns an entity of the named kind at coordinates (no per-entity data yet) --
    [Fact]
    public void SummonCommandSpawnsEntityAtCoordinates() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        server.CommandDispatcher.RegisterSummon();
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(client.Id, out var context));
        var sender = server.DefaultWorld.Ecs.Get<SenderEntityComponent>(context.Entity);

        var fallingQuery = new QueryDescription().WithAll<FallingBlockEntityComponent>();
        List<ArchEntity> Falling() {
            var list = new List<ArchEntity>();
            server.DefaultWorld.Ecs.Query(in fallingQuery, (ArchEntity e, ref FallingBlockEntityComponent _) => list.Add(e));
            return list;
        }
        Assert.Empty(Falling());

        _ = server.CommandDispatcher.ExecuteAsync(sender, "summon sharpminerals:falling_block 1 20 2", client);
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler

        // One falling_block now exists at the exact coordinates, carrying the default "missing" block (no data yet).
        var spawned = Assert.Single(Falling());
        var t = server.DefaultWorld.Ecs.Get<TransformEntityComponent>(spawned);
        Assert.Equal(1.0, t.X, 3);
        Assert.Equal(20.0, t.Y, 3);
        Assert.Equal(2.0, t.Z, 3);
        Assert.Equal(CoreMod.Missing, server.DefaultWorld.Ecs.Get<FallingBlockEntityComponent>(spawned).Block);

        // Players can't be summoned - the registry has the kind, but the command refuses it.
        _ = server.CommandDispatcher.ExecuteAsync(sender, "summon sharpminerals:player 0 20 0", client);
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler
        var playerQuery = new QueryDescription().WithAll<PlayerEntityComponent>();
        Assert.Equal(1, server.DefaultWorld.Ecs.CountEntities(in playerQuery)); // still just the joined Steve
    }

    // -- InventoryComponent.Add splits a large count across slots, capped at the item's max stack size --
    [Fact]
    public void InventoryAddRespectsMaxStackSize() {
        var inv = new InventoryComponent(InventoryEntityComponent.MainSize);

        // 100 stone (max 64) -> 64 + 36 across two slots, nothing left over (the old Add over-stuffed one slot).
        Assert.True(inv.Add(new ItemStack(VanillaMod.Stone, 100)).IsEmpty);
        Assert.Equal(64, inv[0].Count);
        Assert.Equal(36, inv[1].Count);

        // Adding more tops up the partial slot first, then spills into a fresh one.
        Assert.True(inv.Add(new ItemStack(VanillaMod.Stone, 30)).IsEmpty);
        Assert.Equal(64, inv[1].Count); // 36 -> 64
        Assert.Equal(2, inv[2].Count);  // the remaining 2

        // When the range is full, the overflow is returned rather than over-stacked.
        var small = new InventoryComponent(1);
        var leftover = small.Add(new ItemStack(VanillaMod.Stone, 100));
        Assert.Equal(64, small[0].Count);
        Assert.Equal(36, leftover.Count);
    }

    // -- Equipment: held item syncs as Set Equipment; off-hand never reaches a legacy client ---------
    [Fact]
    public void EquipmentSyncsAndOffhandSkipsLegacy() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(client.Id, out var context));
        var inv = server.DefaultWorld.Ecs.Get<InventoryEntityComponent>(context.Entity);
        int eid = server.DefaultWorld.Ecs.Get<PlayerEntityComponent>(context.Entity).NetId;

        // Selecting a hotbar slot broadcasts the held item to others as main-hand equipment.
        inv.Main(2) = new ItemStack(VanillaMod.Cobblestone, 1);
        capture.Broadcasts.Clear();
        handler.Handle(client, new SetHeldItemC2S(2));
        server.Scheduler.Run(); // the held-item change is deferred to the tick
        server.FlushSystems();         // equipment-visibility diff broadcasts the changed slot
        Assert.Equal(2, inv.SelectedSlot);
        var held = capture.Broadcasts.OfType<SetEquipmentS2C>().Single();
        Assert.Equal(eid, held.EntityId);
        Assert.Equal(EquipmentSlot.MainHand, held.Slot);
        Assert.Equal<ItemType>(VanillaMod.Cobblestone, held.Item.Type);

        // A container click that moves the held item updates the hand for others - here, picking the held
        // cobblestone up off hotbar slot 2 (chest-window slot 54 + 2) onto the cursor empties the hand.
        var chest = new BlockEntity(server.DefaultWorld, new Vector3i(10, 5, 10), VanillaMod.Chest);
        server.DefaultWorld.SetBlockEntity(chest);
        server.Containers.Open(client.Id, chest);
        int win = client.Sent.OfType<OpenScreenS2C>().Last().WindowId;
        capture.Broadcasts.Clear();
        server.Containers.OnClick(client.Id, new ClickContainerC2S(win, 0, 56, 0, 0)); // left-click held slot -> cursor
        server.FlushSystems(); // equipment-visibility diff broadcasts the now-empty hand
        Assert.True(inv.Held.IsEmpty, "container: held item moved onto the cursor");
        var cleared = capture.Broadcasts.OfType<SetEquipmentS2C>().Single();
        Assert.Equal(EquipmentSlot.MainHand, cleared.Slot);
        Assert.True(cleared.Item.IsEmpty, "container click cleared the hand for other players");

        // Modern wire: Set Equipment 0x55 = entity id, slot byte (Helmet=5, top bit clear), then the item Slot.
        var payload = protocol.EncodePayload(new SetEquipmentS2C(eid, EquipmentSlot.Helmet, new ItemStack(VanillaMod.Cobblestone, 1)));
        using (var ms = new MinecraftStream(new MemoryStream(payload, writable: false))) {
            Assert.Equal(0x55, ms.ReadVarInt());
            Assert.Equal(eid, ms.ReadVarInt());
            Assert.Equal((byte)5, ms.ReadUByte());
            Assert.True(ms.ReadSlotLite() is { } slot && slot.Count == 1, "modern: item Slot encoded with count 1");
        }

        // Legacy wire: 1.5.2 Entity Equipment 0x05 = int entity id, short slot (Helmet -> 4), then a legacy Slot.
        var je61 = new ProtocolJE61();
        var framed = je61.Frame(new SetEquipmentS2C(eid, EquipmentSlot.Helmet, new ItemStack(VanillaMod.Cobblestone, 1)));
        Assert.Equal((byte)0x05, framed[0]);
        using (var ls = new MinecraftStream(new MemoryStream(framed[1..], writable: false))) {
            Assert.Equal(eid, ls.ReadInt());
            Assert.Equal((short)4, ls.ReadShort());
            var (id, count, _) = ls.ReadLegacySlot();
            Assert.True(id != -1 && count == 1, "legacy: item Slot encoded with count 1");
        }

        // The legacy encoder genuinely cannot represent the off-hand (added in 1.9) - so it MUST be filtered.
        Assert.Throws<NotSupportedException>(() =>
            je61.Frame(new SetEquipmentS2C(eid, EquipmentSlot.OffHand, new ItemStack(VanillaMod.Cobblestone, 1))));

        // ...and it is: a legacy in-world client is ALSO in the Play state, so the gate is protocol VERSION.
        var legacyClient = new CaptureNetClient(2, je61) { State = ConnectionState.Play };
        var modernClient = new CaptureNetClient(3, protocol) { State = ConnectionState.Play };
        Assert.False(PlayerVisibility.CanSeeOffhand(legacyClient), "off-hand stays off the legacy wire");
        Assert.True(PlayerVisibility.CanSeeOffhand(modernClient), "modern client renders off-hand");
    }

    // -- World switch: a connected player moves to another world, keeping inventory + network id ---------
    [Fact]
    public void SwitchWorldMovesPlayerAndPreservesInventory() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(client.Id, out var before));
        var oldWorld = before.World;
        before.World.Ecs.Get<InventoryEntityComponent>(before.Entity).Main(5) = new ItemStack(VanillaMod.Cobblestone, 3);
        int eid = before.World.Ecs.Get<PlayerEntityComponent>(before.Entity).NetId;

        client.Sent.Clear();
        var target = server.GetOrCreateWorld("arena", static (name, server) => new World("arena", new FlatChunkGenerator()));
        server.SwitchWorld(client.Id, target);

        // The context now points at the target world, with a live entity reusing the same network id.
        Assert.True(server.TryGetPlayer(client.Id, out var after));
        Assert.Same(target, after.World);
        Assert.NotSame(oldWorld, after.World);
        Assert.True(after.World.Ecs.IsAlive(after.Entity), "entity alive in the new world");
        Assert.False(oldWorld.Ecs.IsAlive(before.Entity), "old entity despawned");
        Assert.Equal(eid, after.World.Ecs.Get<PlayerEntityComponent>(after.Entity).NetId);
        Assert.Equal<ItemType>(VanillaMod.Cobblestone, after.World.Ecs.Get<InventoryEntityComponent>(after.Entity).Main(5).Type);
        // The client was told to respawn into the new dimension and got its fresh chunks.
        var respawn = Assert.IsType<RespawnS2C>(client.Sent.First(m => m is RespawnS2C));
        Assert.Contains(client.Sent, m => m is ChunkDataS2C);
        // The Respawn must carry the TARGET world's key, and it must differ from the world the player came
        // from - a same-key Respawn doesn't reload the 1.20.1 client, so the old world's entities would linger.
        Assert.Equal(target.Name, respawn.WorldName);
        Assert.NotEqual(oldWorld.Name, respawn.WorldName);
        // Switching to the world it's already in is a no-op.
        client.Sent.Clear();
        server.SwitchWorld(client.Id, target);
        Assert.DoesNotContain(client.Sent, m => m is RespawnS2C);
    }

    // -- Persistence: state survives a disconnect/reconnect (in-memory store) --------
    [Fact]
    public void EntityStatePersistsAcrossReconnect() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);

        var handler = new ServerPacketHandler(server);
        var c1 = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(c1);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(c1, new LoginStartC2S("Persist", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(c1.Id, out var h1), "player is present"); // TODO: change name
        ref var t = ref h1.World.Ecs.Get<TransformEntityComponent>(h1.Entity);
        t.X = 40.5; t.Y = 70.0; t.Z = -12.5; t.Yaw = 90f; t.Pitch = 30f;
        h1.World.Ecs.Get<InventoryEntityComponent>(h1.Entity).Main(5) = new ItemStack(VanillaMod.Cobblestone, 7);

        server.RemovePlayer(c1.Id);
        Assert.True(server.PlayerCount == 0, "disconnected");

        // Reconnect with the SAME name -> same offline UUID -> restored state.
        var c2 = new CaptureNetClient(2, protocol) { State = ConnectionState.Login };
        capture.Register(c2);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(c2, new LoginStartC2S("Persist", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(server.TryGetPlayer(c2.Id, out var h2), "player is present"); // TODO: change name
        var t2 = h2.World.Ecs.Get<TransformEntityComponent>(h2.Entity);
        var inv2 = h2.World.Ecs.Get<InventoryEntityComponent>(h2.Entity);
        Assert.True(t2.X == 40.5 && t2.Z == -12.5 && t2.Yaw == 90f && t2.Pitch == 30f,
            "position + rotation restored");
        Assert.True(inv2.Main(5).Type == VanillaMod.Cobblestone && inv2.Main(5).Count == 7,
            "inventory restored");
    }

    // -- Persistence: a live entity round-trips through the generic EntityCodec (the disk blob) --------
    // Also exercises Step 1: a stack's carried block state (red wool's Color) goes through the item Data bag.
    [Fact]
    public void EntityCodecRoundTripsPersistentComponents() {
        var world = new World("codec", new FlatChunkGenerator());
        var ecs = world.Ecs;

        var e1 = world.Spawn(CoreMod.Player, new TransformEntityComponent(1.5, 70.0, -3.25, 45f, 12f));
        ecs.Get<HealthEntityComponent>(e1) = new HealthEntityComponent(15f, 20f);
        var inv = ecs.Get<InventoryEntityComponent>(e1);
        inv.SelectedSlot = 3;
        inv.Main(0) = new ItemStack(VanillaMod.Stone, 64);
        inv.Main(7) = Types.FromVanillaItem(194); // red wool, carrying its Color state

        var blob = EntityCodec.Encode(ecs, e1);

        // Apply onto a fresh blueprint-spawned entity (the restore path).
        var e2 = world.Spawn(CoreMod.Player, new TransformEntityComponent(0, 0, 0));
        EntityCodec.Apply(ecs, e2, blob);

        var t = ecs.Get<TransformEntityComponent>(e2);
        var h = ecs.Get<HealthEntityComponent>(e2);
        var inv2 = ecs.Get<InventoryEntityComponent>(e2);
        Assert.True(t.X == 1.5 && t.Yaw == 45f && t.Pitch == 12f, "transform round-trips");
        Assert.True(h.Current == 15f && h.Max == 20f, "health round-trips");
        Assert.True(inv2.SelectedSlot == 3, "selected slot round-trips");
        Assert.True(inv2.Main(0).Type == VanillaMod.Stone && inv2.Main(0).Count == 64, "plain stack round-trips");
        Assert.True(inv2.Main(7).Type == VanillaMod.Wool && inv2.Main(7).State?.Get(State.Color) == 14,
            "stack with carried block state round-trips through the item Data bag");
    }

    // -- Inventory consume API (held-item consumption, e.g. fueling a machine) --------
    [Fact]
    public void InventoryConsumeRemovesItemsAndClearsEmptiedSlots() {
        var inv = new InventoryComponent(9);
        inv[0] = new ItemStack(VanillaMod.Stone, 5);
        Assert.Equal(2, inv.Consume(0, 2));   // partial take
        Assert.Equal(3, inv[0].Count);
        Assert.Equal(3, inv.Consume(0, 10));  // more than present -> takes the remainder
        Assert.True(inv[0].IsEmpty);           // emptied slot is cleared
        Assert.Equal(0, inv.Consume(0));       // nothing left to take

        var entityInv = new InventoryEntityComponent { SelectedSlot = 2 };
        entityInv.Main(2) = new ItemStack(VanillaMod.Stone, 4);
        Assert.Equal(1, entityInv.ConsumeHeld());      // default 1, from the held (selected) slot
        Assert.Equal(3, entityInv.Main(2).Count);
    }

    // -- Chunk packet: only block entities with a real wire id are sent (data-only BEs stay server-side) --
    [Fact]
    public void TryBlockEntityTypeIdMapsAChestButNotAPlainBlock() {
        var types = new ProtocolJE763().Types;
        Assert.True(types.TryBlockEntityTypeId(VanillaMod.Chest, out var id) && id == 1,
            "chest has a wire block-entity type id (1)");
        Assert.False(types.TryBlockEntityTypeId(VanillaMod.Stone, out _),
            "a plain block has no block-entity type id, so it isn't sent in the chunk packet");
    }

    // -- Persistence: world chunks (blocks, states, chest contents) survive a save/reload --
    [Fact]
    public void WorldChunksPersistThroughStore() {
        var store = new InMemoryWorldStore();
        var pos = new Vector3i(2, 6, 2);
        var chestPos = new Vector3i(3, 6, 3);

        var w1 = new World("save", new FlatChunkGenerator(), store);
        w1.SetBlock(pos, VanillaMod.Wool);
        w1.SetBlockState(pos, new BlockState(VanillaMod.Wool).Set(State.Color, "red"));
        w1.SetBlock(chestPos, VanillaMod.Chest);
        var chest = new BlockEntity(w1, chestPos, VanillaMod.Chest);
        var contents = new InventoryComponent(27);
        contents[0] = new ItemStack(VanillaMod.Stone, 5);
        chest.Add(contents);
        w1.SetBlockEntity(chest);

        Assert.True(w1.Save() >= 1, "a modified chunk was saved");

        // A fresh world over the same store loads the chunk instead of regenerating it.
        var w2 = new World("save", new FlatChunkGenerator(), store);
        Assert.True(w2.GetBlock(pos) == VanillaMod.Wool, "block persisted");
        Assert.True(w2.GetBlockState(pos)?.Get(State.Color) == 14, "block state (wool colour) persisted");
        Assert.True(w2.GetBlock(new Vector3i(0, 0, 0)) == VanillaMod.Bedrock, "generated terrain persisted too");
        var be = w2.GetBlockEntity(chestPos);
        Assert.True(be is { } && be.Type == VanillaMod.Chest
            && be.Get<InventoryComponent>()[0].Type == VanillaMod.Stone && be.Get<InventoryComponent>()[0].Count == 5,
            "chest block entity + contents persisted");
    }

    // -- Persistence: loose world entities (dropped items) survive a world save/reload --
    [Fact]
    public void DroppedItemsPersistThroughWorldSaveAndLoad() {
        var store = new InMemoryWorldStore();
        var w1 = new World("drops", new FlatChunkGenerator(), store);
        w1.SpawnItem(10.5, 65.0, -4.5, default, new ItemStack(VanillaMod.Stone, 9), pickupDelay: 0);
        Assert.Equal(1, w1.SaveEntities());

        // A fresh world over the same store respawns the item where it lay, with its stack intact.
        var w2 = new World("drops", new FlatChunkGenerator(), store);
        Assert.Equal(1, w2.LoadEntities());

        var loaded = w2.Entities.InChunk(SpatialIndex.CellOf(10.5, 65.0, -4.5));
        Assert.Single(loaded);
        var e = loaded.First();
        var pickup = w2.Ecs.Get<PickupEntityComponent>(e);
        var t = w2.Ecs.Get<TransformEntityComponent>(e);
        Assert.True(pickup.Stack.Type == VanillaMod.Stone && pickup.Stack.Count == 9, "item stack restored");
        Assert.True(t.X == 10.5 && t.Y == 65.0 && t.Z == -4.5, "item position restored");
    }

    // -- Persistence: dropped items survive a full server save -> restart (the real wiring) --
    [Fact]
    public void DroppedItemsPersistAcrossServerRestart() {
        var store = new InMemoryWorldStore();
        var protocol = new ProtocolJE763();

        var worlds1 = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator(), store),
        };
        var server1 = new Server(new ServerContext { Worlds = worlds1, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, new CaptureNetServer(protocol));
        server1.DefaultWorld.SpawnItem(8.5, 5.0, 8.5, default, new ItemStack(VanillaMod.Stone, 3), pickupDelay: 0);
        server1.SaveWorlds(); // the real save path: Server.SaveWorlds -> World.Save -> SaveEntities

        // A fresh server over the same store restores the item at construction (Server ctor -> World.LoadEntities).
        var capture = new CaptureNetServer(protocol);
        var worlds2 = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator(), store),
        };
        var server2 = new Server(new ServerContext { Worlds = worlds2, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);

        var loaded = server2.DefaultWorld.Entities.InChunk(SpatialIndex.CellOf(8.5, 5.0, 8.5));
        Assert.Single(loaded);
        var pickup = server2.DefaultWorld.Ecs.Get<PickupEntityComponent>(loaded.First());
        Assert.True(pickup.Stack.Type == VanillaMod.Stone && pickup.Stack.Count == 3, "item restored on the new server");

        // The loaded item gets a net id BEFORE any client connects (announce reaches no one).
        server2.AnnounceSystems();
        // A player then joins (streaming the item's column) and the tracker spawns the existing item to it.
        var handler = new ServerPacketHandler(server2);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Joiner", Guid.Empty));
        Settle(server2); // drive the join-triggered async column streaming to completion
        server2.Scheduler.Run();
        server2.FlushSystems();
        Assert.Contains(client.Sent, m => m is SpawnEntityS2C s && s.Type == CoreMod.Item);
    }

    // -- Item pickup: the collect animation must reach the viewer BEFORE the entity is removed, or the item
    //    just vanishes with no fly-to-player animation. (Regression: the event-driven tracker despawns on
    //    DestroyEntity synchronously, so the pickup must destroy the drop only AFTER broadcasting the collect.) --
    [Fact]
    public void ItemPickupCollectAnimationPrecedesRemoval() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> { ["overworld"] = new World("overworld", new FlatChunkGenerator()) };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20 }, capture);

        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Picker", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        server.Scheduler.Run();

        // A drop right on the player with zero velocity + no pickup delay (deterministic, unlike the random pop of
        // SpawnDroppedItem): it overlaps immediately and is spawned to the viewer on spawn.
        Assert.True(server.TryGetPlayer(client.Id, out var ctx));
        var pos = server.DefaultWorld.Ecs.Get<TransformEntityComponent>(ctx.Entity);
        var drop = server.DefaultWorld.SpawnItem(pos.X, pos.Y, pos.Z, default, new ItemStack(VanillaMod.Cobblestone, 1), pickupDelay: 0);
        int netId = server.DefaultWorld.Ecs.Get<PickupEntityComponent>(drop).EntityId;

        client.Sent.Clear();
        for (int i = 0; i < 5; i++) server.DefaultWorld.Tick(); // collision feedback detects the overlap, pickup collects
        server.FlushSystems();

        int collect = client.Sent.FindIndex(m => m is CollectItemS2C c && c.CollectedEntityId == netId);
        int remove = client.Sent.FindIndex(m => m is RemoveEntitiesS2C r && r.EntityIds.Contains(netId));
        Assert.True(collect >= 0, "collect animation sent to the viewer");
        Assert.True(remove >= 0, "drop removed from the viewer");
        Assert.True(collect < remove, "collect animation precedes the removal");
    }

    // -- Chest lid animation: opening sends a Block Action with viewer count 1 (lid up), closing sends 0 (lid down) --
    [Fact]
    public void ChestOpenAndCloseAnimateTheLid() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> { ["overworld"] = new World("overworld", new FlatChunkGenerator()) };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20 }, capture);

        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Opener", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        server.Scheduler.Run();

        var chest = new BlockEntity(server.DefaultWorld, new Vector3i(2, 5, 2), VanillaMod.Chest); // near the opener (in broadcast range)
        server.DefaultWorld.SetBlockEntity(chest);

        capture.Broadcasts.Clear();
        server.Containers.Open(client.Id, chest);
        Assert.Contains(capture.Broadcasts.OfType<BlockActionS2C>(),
            b => b.Position == chest.Position && b.ActionId == 1 && b.Param == 1); // one viewer -> lid opens

        int win = client.Sent.OfType<OpenScreenS2C>().Last().WindowId;
        capture.Broadcasts.Clear();
        server.Containers.OnClose(client.Id, win);
        Assert.Contains(capture.Broadcasts.OfType<BlockActionS2C>(),
            b => b.Position == chest.Position && b.ActionId == 1 && b.Param == 0); // last viewer left -> lid closes
    }

    // -- Entity tracker: per-player visibility - spawn in view, cull out of view, remove on destroy --
    [Fact]
    public void EntityTrackerSpawnsInViewItemsAndRemovesThemOutOfView() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> { ["overworld"] = new World("overworld", new FlatChunkGenerator()) };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20 }, capture);

        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Viewer", Guid.Empty)); // spawns at column (0,0)
        Settle(server); // drive the join-triggered async column streaming to completion
        server.Scheduler.Run();

        // One item in the viewer's columns, one far outside them (column ~100,100). Each is spawned to in-view
        // clients synchronously on spawn now, so clear FIRST and read the ids the tracker stamped afterwards.
        client.Sent.Clear();
        var near = server.DefaultWorld.SpawnItem(2.5, 5.0, 2.5, default, new ItemStack(VanillaMod.Stone, 1), pickupDelay: 100);
        var far = server.DefaultWorld.SpawnItem(1600.5, 5.0, 1600.5, default, new ItemStack(VanillaMod.Cobblestone, 1), pickupDelay: 100);

        int nearId = server.DefaultWorld.Ecs.Get<PickupEntityComponent>(near).EntityId;
        int farId = server.DefaultWorld.Ecs.Get<PickupEntityComponent>(far).EntityId;
        Assert.Contains(client.Sent, m => m is SpawnEntityS2C s && s.EntityId == nearId);     // in view -> spawned
        Assert.DoesNotContain(client.Sent, m => m is SpawnEntityS2C s && s.EntityId == farId); // out of view -> not sent

        // Destroying the in-view item makes the next flush remove it from the client.
        client.Sent.Clear();
        server.DefaultWorld.DestroyEntity(near);
        server.FlushSystems();
        Assert.Contains(client.Sent, m => m is RemoveEntitiesS2C r && r.EntityIds.Contains(nearId));
    }

    // -- Entity tracker: two players in the same columns are spawned to each other --
    [Fact]
    public void EntityTrackerSpawnsPlayersToEachOther() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> { ["overworld"] = new World("overworld", new FlatChunkGenerator()) };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20 }, capture);

        var handler = new ServerPacketHandler(server);
        var ca = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        var cb = new CaptureNetClient(2, protocol) { State = ConnectionState.Login };
        capture.Register(ca);
        capture.Register(cb);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(ca, new LoginStartC2S("Alice", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(cb, new LoginStartC2S("Bob", Guid.Empty)); // Bob's join spawns him to Alice and Alice to him
        Settle(server); // drive the join-triggered async column streaming to completion
        server.Scheduler.Run();

        Assert.True(server.TryGetPlayer(ca.Id, out var pa), "Alice present");
        Assert.True(server.TryGetPlayer(cb.Id, out var pb), "Bob present");
        int aId = server.DefaultWorld.Ecs.Get<PlayerEntityComponent>(pa.Entity).NetId;
        int bId = server.DefaultWorld.Ecs.Get<PlayerEntityComponent>(pb.Entity).NetId;
        Assert.Contains(ca.Sent, m => m is SpawnPlayerS2C s && s.EntityId == bId); // Alice sees Bob
        Assert.Contains(cb.Sent, m => m is SpawnPlayerS2C s && s.EntityId == aId); // Bob sees Alice
    }

    // -- Persistence: the length-prefixed component bag round-trips known components and skips unknown ones --
    [Fact]
    public void ComponentBagRoundTripsKnownAndSkipsUnknownComponents() {
        // Known component: an inventory survives Write -> Read through the bag, and a value written AFTER the bag
        // is still readable (the length prefixes keep the stream aligned).
        var src = new ComponentObject();
        var inv = new InventoryComponent(9);
        inv[2] = new ItemStack(VanillaMod.Stone, 7);
        src.Add(inv);

        using var ms = new MemoryStream();
        var w = new MinecraftStream(ms);
        ComponentBag.Write(w, src);
        w.WriteString("after-bag");

        ms.Position = 0;
        var r = new MinecraftStream(ms);
        var dst = new ComponentObject();
        ComponentBag.Read(r, dst);
        Assert.True(dst.TryGet<InventoryComponent>(out var got) && got[2].Type == VanillaMod.Stone && got[2].Count == 7,
            "inventory round-tripped through the bag");
        Assert.Equal("after-bag", r.ReadString());

        // Unknown component: an id with no registered reader (a removed mod's) is skipped using its length prefix,
        // and the value after it is still readable - no desync. The raw blob is PRESERVED, not lost.
        using var ms2 = new MemoryStream();
        var w2 = new MinecraftStream(ms2);
        w2.WriteVarInt(1);                          // one component in the bag
        w2.WriteString("ghost:removed_component");  // ...whose type is no longer registered
        w2.WriteVarInt(3);
        w2.Write(new byte[] { 1, 2, 3 }, 0, 3);     // its opaque, unreadable blob
        w2.WriteString("after-unknown");            // trailing data

        ms2.Position = 0;
        var r2 = new MinecraftStream(ms2);
        var dst2 = new ComponentObject();
        ComponentBag.Read(r2, dst2);
        Assert.False(dst2.Has<InventoryComponent>(), "the unknown component was not materialized as a real component");
        Assert.Equal("after-unknown", r2.ReadString());

        // ...but it round-trips: writing dst2 back re-emits the unknown [id][blob] verbatim (non-destructive,
        // so re-adding the mod restores it).
        using var ms3 = new MemoryStream();
        var w3 = new MinecraftStream(ms3);
        ComponentBag.Write(w3, dst2);
        ms3.Position = 0;
        var r3 = new MinecraftStream(ms3);
        Assert.Equal(1, r3.ReadVarInt());
        Assert.Equal("ghost:removed_component", r3.ReadString());
        Assert.Equal(3, r3.ReadVarInt());
        Assert.Equal(new byte[] { 1, 2, 3 }, r3.ReadBytes(3));
    }

    // -- Persistence: a chunk only saves once it has been modified ------------------
    [Fact]
    public void GeneratedChunksPersistAndLoadedChunksAreClean() {
        var store = new InMemoryWorldStore();
        var world = new World("dirtytest", new FlatChunkGenerator(), store);
        world.GetBlock(new Vector3i(0, 0, 0)); // generate a chunk, no gameplay change
        Assert.True(world.Save() == 1, "a freshly generated chunk is dirty, so worldgen output is persisted");
        Assert.True(world.Save() == 0, "saving clears the dirty flag");
        world.SetBlock(new Vector3i(1, 6, 1), VanillaMod.Stone);
        Assert.True(world.Save() == 1, "a gameplay edit marks the chunk dirty");
        Assert.True(world.Save() == 0, "saving clears the dirty flag");

        // A chunk read back from the store is the clean baseline (ClearDirty on the load path), not re-persisted.
        var reloaded = new World("dirtytest", new FlatChunkGenerator(), store);
        reloaded.GetBlock(new Vector3i(0, 0, 0)); // hits the store, not the generator
        Assert.True(reloaded.Save() == 0, "a chunk loaded from the store is not dirty");
    }

    // -- Chunk streaming: the view follows the player across chunk boundaries --------
    [Fact]
    public void ChunkStreamingFollowsPlayer() {
        var protocol = new ProtocolJE763();
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("t"), MaxPlayers = 20, TicksPerSecond = 20,
        }, capture);
        var handler = new ServerPacketHandler(server);
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);
        DetachSyncContext(); // detach BEFORE the join so its async-void streaming can never gate completion
        handler.Handle(client, new LoginStartC2S("Walker", Guid.Empty));
        Settle(server); // drive the join-triggered async column streaming to completion
        Assert.True(client.Sent.OfType<ChunkDataS2C>().Any(), "initial view streamed on join");
        var joinTeleport = client.Sent.OfType<SynchronizePlayerPositionS2C>().First();

        // Before confirming the join teleport, the client's position is stale -> ignored (no stream).
        client.Sent.Clear();
        handler.Handle(client, new SetPlayerPositionC2S(40.0, WorldDefaults.SurfaceY, 0.5, true));
        server.FlushSystems(); // streaming is now a per-tick system, not synchronous on the move packet
        Assert.True(!client.Sent.OfType<ChunkDataS2C>().Any(), "position ignored while teleport unconfirmed");

        // Confirm the teleport; now a move into a new column streams (spawn chunk 0,0; x=40 -> chunk 2).
        handler.Handle(client, new ConfirmTeleportationC2S(joinTeleport.TeleportId));
        client.Sent.Clear();
        handler.Handle(client, new SetPlayerPositionC2S(40.0, WorldDefaults.SurfaceY, 0.5, true));
        server.FlushSystems();
        Settle(server); // the newly-visible columns stream via the same async SendColumnWhenReady path
        Assert.True(client.Sent.OfType<SetCenterChunkS2C>().Any(c => c.ChunkX == 2 && c.ChunkZ == 0),
            "recenters on the new chunk");
        Assert.True(client.Sent.OfType<ChunkDataS2C>().Any(), "streams newly-visible columns");

        // Moving within the same column streams nothing new.
        client.Sent.Clear();
        handler.Handle(client, new SetPlayerPositionC2S(41.0, WorldDefaults.SurfaceY, 1.0, true));
        server.FlushSystems();
        Settle(server);
        Assert.True(!client.Sent.OfType<ChunkDataS2C>().Any(), "no chunks while staying in a column");
    }

    // -- EventBus: polymorphic dispatch (heavy-context + generic events together) ----
    [Fact]
    public void EventBusDispatchesToBaseTypesAndInterfaces() {
        var bus = BareServer().Events;
        var fired = new List<string>();
        bus.Subscribe<DamageEvent>(_ => fired.Add("any-damage"));        // generic
        bus.Subscribe<ZombieDamage>(e => fired.Add($"zombie:{e.Amount}")); // specific, full context
        bus.Subscribe<IAudited>(_ => fired.Add("audit"));                // cross-cutting interface

        bus.Publish(new ZombieDamage(5)); // IS-A DamageEvent and IAudited

        Assert.Contains("any-damage", fired);
        Assert.Contains("zombie:5", fired);
        Assert.Contains("audit", fired);

        // Publishing the base must NOT reach the derived (specific) handler.
        fired.Clear();
        bus.Publish(new DamageEvent());
        Assert.Contains("any-damage", fired);
        Assert.DoesNotContain(fired, f => f.StartsWith("zombie"));
    }

    // -- EventBus: deferred publish only fires when the queue is drained -------------
    [Fact]
    public void DeferredEventsProcessOnlyOnDrain() {
        var server = BareServer();
        var bus = server.Events;
        int count = 0;
        bus.Subscribe<DamageEvent>(_ => count++);

        bus.PublishDeferred(new DamageEvent());
        Assert.Equal(0, count); // queued, not yet run

        server.Scheduler.Run();
        Assert.Equal(1, count); // ran on drain (the "tick thread")

        server.Scheduler.Run();
        Assert.Equal(1, count); // nothing left to run
    }

    // -- Async persistence: write-behind reads-after-write and flushes on dispose ----
    [Fact]
    public void AsyncEntityStoreReadsAfterWriteThenFlushes() {
        var inner = new InMemoryEntityStore();
        var id = Guid.NewGuid();
        var data = new byte[] { 1, 2, 3, 4 };

        using (var store = new AsyncEntityStore(inner)) {
            store.Save(id, data); // queued, not necessarily flushed yet
            Assert.True(store.TryLoad(id, out var read) && read.Length == 4 && read[0] == 1,
                "read-after-write sees the queued value before flush");
        } // Dispose drains the queue to the inner store

        Assert.True(inner.TryLoad(id, out var flushed) && flushed.Length == 4 && flushed[0] == 1,
            "queued write was flushed to the inner store on dispose");
    }

    // -- Chunk eviction: drop out-of-range chunks, saving dirty ones first ----------
    [Fact]
    public void ChunkEvictionDropsAndSavesOutOfRangeChunks() {
        var store = new InMemoryWorldStore();
        var world = new World("evict", new FlatChunkGenerator(), store);
        world.SetBlock(new Vector3i(1, 6, 1), VanillaMod.Stone); // chunk (0,0,0) - dirty
        world.GetBlock(new Vector3i(1600, 4, 0));                   // chunk (100,0,0) - generated, clean
        int before = world.LoadedChunkCount;
        Assert.True(before >= 2, "two columns loaded");

        // Keep only column (0,0) within radius 1 - the far column is dropped.
        int evicted = world.EvictChunks(new List<(long, long)> { (0, 0) }, keepRadius: 1);
        Assert.True(evicted >= 1 && world.LoadedChunkCount < before, "far chunk evicted");
        Assert.True(world.GetChunk(new Vector3i(0, 0, 0)) is not null, "kept chunk still loaded");

        // Evict everything (no centres) - the dirty near chunk must be saved before it goes.
        world.EvictChunks(new List<(long, long)>(), keepRadius: 1);
        Assert.True(store.TryLoadChunk("evict", new Vector3i(0, 0, 0), out _),
            "dirty chunk was saved on eviction");
    }

    // -- Chunk eviction: never drops a dirty chunk when there's no store to save it --
    [Fact]
    public void ChunkEvictionKeepsDirtyChunkWithoutStore() {
        var world = new World("nostore", new FlatChunkGenerator()); // no store
        world.SetBlock(new Vector3i(1, 6, 1), VanillaMod.Stone);  // dirty chunk (0,0,0)
        int evicted = world.EvictChunks(new List<(long, long)>(), keepRadius: 1); // try to evict all
        Assert.True(evicted == 0 && world.LoadedChunkCount >= 1, "dirty chunk kept (no store to save to)");
    }

    // -- TypeMapper abstraction: per-protocol mapper, and codecs map via the stream -----
    [Fact]
    public void ProtocolExposesMapperAndCodecsUseIt() {
        var protocol = new ProtocolJE763();
        // The mapper is now exposed per-protocol (was a static class welded to JE763).
        Assert.True(protocol.Types.StateId(VanillaMod.Stone) == 1, "protocol exposes its type mapper");

        // A container packet carries domain ItemStacks; the codec maps them via the mapper that
        // EncodePayload sets on the stream - this would NullReference in SlotWire if the seam broke.
        var content = new SetContainerContentS2C(0, 0,
            new[] { new ItemStack(VanillaMod.Stone, 5), protocol.Types.FromVanillaItem(194) /* red wool */ },
            default);
        var bytes = protocol.EncodePayload(content);
        Assert.True(bytes.Length > 0, "SetContainerContent encoded via the protocol's mapper");
    }

    // -- Broadcast cache: a message is encoded once per protocol version ------------
    [Fact]
    public void BroadcastPacketEncodesOncePerVersion() {
        var protocol = new ProtocolJE763();
        var packet = new CachedPacket(new BlockUpdateS2C(new Vector3i(1, 2, 3), VanillaMod.Stone));
        var first = packet.Framed(protocol);
        var second = packet.Framed(protocol);
        Assert.Same(first, second); // 2nd call hits the per-version cache - no re-encode
        Assert.True(first.Length > 0, "produced framed wire bytes");
    }

    // -- Rain: one intermediary message lowers to several Game Event (0x1F) packets ---
    [Fact]
    public void RainLowersToGameEventPackets() {
        var protocol = new ProtocolJE763();

        // Reads back every [VarInt len][VarInt id][UByte event][Float value] frame concatenated by Frame.
        static List<(int Id, byte Event, float Value)> ReadEvents(byte[] framed) {
            using var s = new MinecraftStream(new MemoryStream(framed, writable: false));
            var events = new List<(int, byte, float)>();
            while (s.Position < framed.Length) {
                int len = s.ReadVarInt();
                long end = s.Position + len;
                events.Add((s.ReadVarInt(), s.ReadUByte(), s.ReadFloat()));
                Assert.Equal(end, s.Position); // each frame's declared length is exact
            }
            return events;
        }

        const int GameEvent = 0x1F;

        // Rain -> Begin Raining (event 2) then a Rain Level Change (event 7) carrying the strength.
        Assert.Equal(
            new[] { (GameEvent, (byte)2, 0f), (GameEvent, (byte)7, 0.5f) },
            ReadEvents(protocol.Frame(new RainS2C(RainType.Rain, 0.5f))));

        // Thunderstorm additionally raises the thunder gradient (event 8).
        Assert.Equal(
            new[] { (GameEvent, (byte)2, 0f), (GameEvent, (byte)7, 1f), (GameEvent, (byte)8, 1f) },
            ReadEvents(protocol.Frame(new RainS2C(RainType.Thunderstorm, 1f))));

        // Clear weather -> a single End Raining (event 1).
        Assert.Equal(
            new[] { (GameEvent, (byte)1, 0f) },
            ReadEvents(protocol.Frame(new RainS2C(RainType.None, 0f))));

        // The intermediary has no direct codec, but the protocol still reports it as encodable.
        Assert.True(protocol.CanEncode(new RainS2C(RainType.Rain, 0.5f)));
    }

    // -- Legacy (JE61 / 1.5.2) framing: detection + non-VarInt wire format ----------
    [Fact]
    public void LegacyProtocolDetectionAndFraming() {
        var registry = new ProtocolRegistry(new ProtocolJE763(), new ProtocolJE762(), new ProtocolJE61());

        // First-byte detection: legacy markers route to JE61; a normal modern frame length stays modern.
        Assert.Equal(61, registry.Detect(0xFE).Version);  // legacy server-list ping
        Assert.Equal(61, registry.Detect(0x02).Version);  // legacy handshake
        Assert.Equal(763, registry.Detect(0x10).Version); // modern frame length

        var je61 = registry.Detect(0xFE);

        // Serverbound ping [0xFE][0x01] decodes with NO length prefix, straight off the live stream.
        using var ping = new MinecraftStream(new MemoryStream(new byte[] { 0xFE, 0x01 }, writable: false));
        Assert.Equal(new LegacyServerListPingC2S(1),
            je61.ReadMessage(ping, ConnectionState.Handshaking, PacketDirection.Serverbound));

        // Clientbound kick/ping-response: [0xFF][short charcount][UTF-16BE], no length prefix; round-trips.
        var kick = new LegacyKickS2C("§1\0test");
        byte[] framed = je61.Frame(kick);
        Assert.Equal((byte)0xFF, framed[0]);
        using var back = new MinecraftStream(new MemoryStream(framed, writable: false));
        Assert.Equal(kick, je61.ReadMessage(back, ConnectionState.Handshaking, PacketDirection.Clientbound));

        // An unknown legacy id can't be skipped (no length) -> bail rather than desync.
        using var bad = new MinecraftStream(new MemoryStream(new byte[] { 0x99 }, writable: false));
        Assert.Throws<FormatException>(() =>
            je61.ReadMessage(bad, ConnectionState.Handshaking, PacketDirection.Serverbound));
    }

    [Fact]
    public void PeekByteIsReServed() {
        // A peeked byte is returned again by the next read, so a sniffed connection decodes identically.
        using var s = new MinecraftStream(new MemoryStream(new byte[] { 0x2A, 0x07 }, writable: false));
        Assert.Equal((byte)0x2A, s.PeekUByte());
        Assert.Equal((byte)0x2A, s.ReadUByte());
        Assert.Equal((byte)0x07, s.ReadUByte());
    }

    [Fact]
    public void LegacyString16RoundTrips() {
        using var ms = new MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true);
        w.WriteString16("§1\0hi");
        ms.Position = 0;
        Assert.Equal("§1\0hi", new MinecraftStream(ms).ReadString16());
    }

    // -- Legacy login: AES/CFB8 transport + login codecs ---------------------------
    [Fact]
    public void LegacyAesCfb8RoundTrips() {
        var key = new byte[16];
        for (int i = 0; i < 16; i++) key[i] = (byte)(i * 7 + 1);
        var plain = System.Text.Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog 0123456789!");

        var cipherMs = new MemoryStream();
        new AesCfb8Stream(cipherMs, key).Write(plain, 0, plain.Length); // encrypt -> cipherMs
        byte[] cipher = cipherMs.ToArray();
        Assert.Equal(plain.Length, cipher.Length);  // CFB8 is a stream cipher (no padding)
        Assert.NotEqual(plain, cipher);              // actually encrypted

        var dec = new AesCfb8Stream(new MemoryStream(cipher), key);
        var outBuf = new byte[plain.Length];
        for (int read = 0; read < plain.Length;) {
            int r = dec.Read(outBuf, read, plain.Length - read);
            if (r <= 0) break;
            read += r;
        }
        Assert.Equal(plain, outBuf); // decrypt round-trips (key = IV)
    }

    [Fact]
    public void LegacyLoginCodecsRoundTrip() {
        var je61 = new ProtocolJE61();

        // Handshake 0x02 decode (single-byte id, fields straight off the stream).
        var hsMs = new MemoryStream();
        var w = new MinecraftStream(hsMs, leaveOpen: true);
        w.WriteUByte(0x02); w.WriteUByte(61); w.WriteString16("Steve"); w.WriteString16("localhost"); w.WriteInt(25565);
        hsMs.Position = 0;
        var hs = Assert.IsType<LegacyHandshakeC2S>(
            je61.ReadMessage(new MinecraftStream(hsMs), ConnectionState.Handshaking, PacketDirection.Serverbound));
        Assert.Equal((byte)61, hs.ProtocolVersion);
        Assert.Equal("Steve", hs.Username);
        Assert.Equal(25565, hs.Port);

        // Clientbound framing carries the right single-byte ids, no length prefix.
        Assert.Equal((byte)0x01, je61.Frame(new LegacyLoginRequestS2C(7, "flat", 1, 0, 1, 20))[0]);
        Assert.Equal((byte)0xFD, je61.Frame(new LegacyEncryptionRequestS2C("-", je61.PublicKeyDer, new byte[] { 1, 2, 3, 4 }))[0]);

        // The server's RSA round-trips a PKCS#1 blob (what the 0xFC decrypt path relies on).
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(je61.PublicKeyDer, out _);
        var secret = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 1, 2, 3, 4, 5, 6 };
        var encrypted = rsa.Encrypt(secret, System.Security.Cryptography.RSAEncryptionPadding.Pkcs1);
        Assert.Equal(secret, je61.DecryptRsa(encrypted));
    }

    [Fact]
    public void LegacyChunkSerializerBuildsFlatColumn() {
        var world = new World("test", new FlatChunkGenerator());
        var je61 = new ProtocolJE61();
        var chunk = LegacyChunkSerializer.Build(je61.Types, world, 0, 0);

        Assert.True((chunk.PrimaryBitmap & 1) != 0, "surface section present");
        Assert.Equal(0, chunk.AddBitmap);
        Assert.True(chunk.GroundUpContinuous);

        // Decompress; payload = present-section count x (blocks 4096 + 3 nibble arrays 2048) + biome 256.
        using var inflated = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(
                   new MemoryStream(chunk.CompressedData), System.IO.Compression.CompressionMode.Decompress))
            z.CopyTo(inflated);
        int sections = System.Numerics.BitOperations.PopCount((uint)chunk.PrimaryBitmap);
        Assert.Equal(sections * (4096 + 2048 * 3) + 256, (int)inflated.Length);

        Assert.Equal((byte)0x33, je61.Frame(chunk)[0]); // single-byte id, no length prefix
    }

    // -- Legacy chat (0x03): serverbound -> generic; clientbound component -> §-coded string ----
    [Fact]
    public void LegacyChatRoundTrips() {
        var je61 = new ProtocolJE61();

        // Serverbound 0x03 Chat -> generic ChatMessageC2S, raw text (keeps a leading '/').
        var ms = new MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true);
        w.WriteUByte(0x03); w.WriteString16("/help");
        ms.Position = 0;
        var msg = Assert.IsType<ChatMessageC2S>(
            je61.ReadMessage(new MinecraftStream(ms), ConnectionState.Handshaking, PacketDirection.Serverbound));
        Assert.Equal("/help", msg.Message);

        // Clientbound SystemChatMessageS2C -> 0x03 with a §-coded string (colour + formatting).
        var styled = new TextComponent("hi") { Color = "red", Bold = true };
        var framed = je61.Frame(new SystemChatMessageS2C(styled, Overlay: false));
        Assert.Equal((byte)0x03, framed[0]);
        Assert.Equal("§c§lhi", new MinecraftStream(new MemoryStream(framed[1..], writable: false)).ReadString16());

        // Nested Extra is concatenated; a modern hex colour quantizes to the nearest § colour.
        var compound = new TextComponent("a") { Color = "#ff5555" };       // ~ red -> §c
        compound.With(new TextComponent("b") { Color = "green" });      // -> §a
        var cf = je61.Frame(new SystemChatMessageS2C(compound, Overlay: false));
        Assert.Equal("§ca§ab", new MinecraftStream(new MemoryStream(cf[1..], writable: false)).ReadString16());
    }

    // -- Chat JSON: a nested Extra component keeps its subclass fields (regression) ----
    [Fact]
    public void ChatComponentSerializesNestedExtraFields() {
        // The crash: Extra is List<ChatComponent>, so STJ used to serialize each element against the base
        // type and drop TextComponent.Text - the styled child collapsed to {"color":"dark_purple"}, which
        // the client rejected ("Don't know how to turn {...} into a Component"). The runtime-type dispatch
        // must now emit the child's text.
        var message = ChatComponent.Text("<")
            .With(ChatComponent.Text("Server").SetColor(TextColor.DarkPurple), ChatComponent.Text("> hi"));
        var json = message.ToString();
        Assert.Contains("\"text\":\"Server\"", json);
        Assert.Contains("\"color\":\"dark_purple\"", json);
        Assert.DoesNotContain("{\"color\":\"dark_purple\"}", json); // never a bare style-only child

        // ...and it round-trips back to the same tree, with subclass types preserved.
        var back = ChatComponent.FromJson(json);
        var root = Assert.IsType<TextComponent>(back);
        Assert.Equal("<", root.Text);
        Assert.NotNull(root.Extra);
        var server = Assert.IsType<TextComponent>(root.Extra![0]);
        Assert.Equal("Server", server.Text);
        Assert.Equal("dark_purple", server.Color);
    }

    // -- Block drops: keep item-identity state (colour), reset placement state (facing) ----
    [Fact]
    public void DropStateKeepsColourResetsFacing() {
        // A facing chest's drop resets facing to default -> all-default -> carries no state on the drop.
        var chest = new BlockState(VanillaMod.Chest).Set(State.Facing, "east");
        var chestDrop = chest.ForDrop();
        Assert.Equal(0, chestDrop.Get(State.Facing));
        Assert.True(chestDrop.Matches(new BlockState(VanillaMod.Chest)), "facing-only state => stateless drop");

        // Red wool keeps its colour, so the drop carries it (a non-default, item-identity state).
        var wool = new BlockState(VanillaMod.Wool).Set(State.Color, "red");
        var woolDrop = wool.ForDrop();
        Assert.Equal(State.Color.IndexOf("red"), woolDrop.Get(State.Color));
        Assert.False(woolDrop.Matches(new BlockState(VanillaMod.Wool)), "colour kept => stateful drop");
    }

    // -- Physics: a generic entity falls under gravity and rests on terrain ---------
    [Fact]
    public void DroppedItemFallsAndRestsOnGround() {
        var world = new World("physics", new FlatChunkGenerator());
        // Spawn well above the flat surface (grass occupies y=4, so its top face is y=5).
        var entity = world.SpawnDroppedItem(new Vector3i(0, 12, 0), new ItemStack(VanillaMod.Stone, 1));
        for (int i = 0; i < 60; i++) world.Tick();

        var t = world.Ecs.Get<TransformEntityComponent>(entity);
        var v = world.Ecs.Get<VelocityEntityComponent>(entity);
        Assert.True(System.Math.Abs(t.Y - 5.0) < 1e-3, $"item rests on the surface (Y={t.Y}, expected 5)");
        Assert.True(System.Math.Abs(v.Y) < 1e-6, "vertical velocity settled to rest");
    }

    // -- Physics: terrain collision stops horizontal motion at a wall ---------------
    [Fact]
    public void PhysicsStopsAtWalls() {
        var world = new World("walls", new FlatChunkGenerator());
        world.SetBlock(new Vector3i(1, 5, 0), VanillaMod.Stone); // a wall east of the spawn

        var entity = world.SpawnDroppedItem(new Vector3i(0, 5, 0), new ItemStack(VanillaMod.Stone, 1));
        world.Ecs.Get<VelocityEntityComponent>(entity) = new VelocityEntityComponent(0.5, 0, 0); // override the random scatter: shove it due east
        for (int i = 0; i < 20; i++) world.Tick();

        var t = world.Ecs.Get<TransformEntityComponent>(entity);
        // The wall's west face is x=1, so the box's right edge can't push past it (read the actual
        // collider half-width so the test tracks the item's real size, not a hardcoded one).
        var hw = world.Ecs.Get<HitboxEntityComponent>(entity).HalfWidth;
        Assert.True(t.X + hw <= 1.0 + 1e-3, $"item stopped at the wall (X={t.X}, hw={hw})");
    }

    // -- Collision: a spawned player's CollisionFeedback is reliably constructed (no null list) --
    [Fact]
    public void PlayerCollisionFeedbackIsInitialized() {
        // The production NRE came from a parameterless struct ctor (bypassed under the Release JIT) leaving
        // Touching null. It's now a per-tick scratch buffer that CollisionFeedbackSystem (its only writer)
        // lazily creates, so it's null until the first collision pass - and that pass never dereferences null.
        var world = new World("collide", new FlatChunkGenerator());
        var player = world.SpawnPlayer(1, "P", Guid.NewGuid(), 1);

        Assert.Null(Record.Exception(() => world.Tick()));                        // collision pass runs on a fresh player, no NRE
        Assert.NotNull(world.Ecs.Get<CollisionEntityComponent>(player).Touching); // lazily created by that first pass
    }

    // -- Spatial index: chunk-bucketed lookups + incremental move/remove maintenance --------
    [Fact]
    public void SpatialIndexBucketsAndQueries() {
        var world = new World("idx", new FlatChunkGenerator());
        var idx = world.Entities;

        var a = world.Ecs.Create(new TransformEntityComponent(2, 5, 2));    // chunk-cube (0,0,0)
        var b = world.Ecs.Create(new TransformEntityComponent(40, 5, 2));   // chunk-cube (2,0,0), 38 blocks away
        idx.Add(a, 2, 5, 2);
        idx.Add(b, 40, 5, 2);

        // Ranged lookup: only the near entity is within radius.
        var near = new List<ArchEntity>();
        idx.Near(2, 5, 2, 3.0, near);
        Assert.Contains(a, near);
        Assert.DoesNotContain(b, near);

        // Per-chunk lookup buckets them separately.
        Assert.Contains(a, idx.InChunk(new Vector3i(0, 0, 0)));
        Assert.Contains(b, idx.InChunk(new Vector3i(2, 0, 0)));

        // Move 'a' across chunk boundaries -> it re-buckets (the EntityMoved-driven path).
        idx.Update(a, 200, 5, 2); // chunk-cube (12,0,0)
        Assert.DoesNotContain(a, idx.InChunk(new Vector3i(0, 0, 0)));
        Assert.Contains(a, idx.InChunk(new Vector3i(12, 0, 0)));

        // Remove drops it entirely.
        idx.Remove(b);
        Assert.DoesNotContain(b, idx.InChunk(new Vector3i(2, 0, 0)));
    }

    // -- Spatial index: spawn/despawn keep it consistent through the World lifecycle ---------
    [Fact]
    public void SpawnAndDespawnMaintainSpatialIndex() {
        var world = new World("idxspawn", new FlatChunkGenerator());
        var cell = new Vector3i(0, 0, 0); // block (5,6,5) -> chunk-cube (0,0,0)

        var item = world.SpawnDroppedItem(new Vector3i(5, 6, 5), new ItemStack(VanillaMod.Stone, 1));
        Assert.Contains(item, world.Entities.InChunk(cell));

        world.DestroyEntity(item);
        Assert.DoesNotContain(item, world.Entities.InChunk(cell));
    }

    // -- Events: a non-player entity raises the generic EntityMoved when physics moves it ----
    [Fact]
    public void NonPlayerEntityRaisesEntityMoved() {
        var world = new World("emove", new FlatChunkGenerator());
        var server = BareServer(("emove", world)); // wires world.Events + NextEntityId + drains via the scheduler
        var moved = new List<ArchEntity>();
        server.Events.Subscribe<EntityMoved>(e => moved.Add(e.Entity));

        // Spawn above the surface so gravity actually shifts it; pin a known scatter so it moves.
        var item = world.SpawnDroppedItem(new Vector3i(0, 12, 0), new ItemStack(VanillaMod.Stone, 1));

        for (int i = 0; i < 5; i++) { world.Tick(); server.Scheduler.Run(); }
        Assert.Contains(item, moved); // the falling item published EntityMoved (deferred -> drained)

        // Once it settles, the move events stop (MoveEpsilon rest cut-off).
        for (int i = 0; i < 60; i++) { world.Tick(); server.Scheduler.Run(); }
        moved.Clear();
        for (int i = 0; i < 5; i++) { world.Tick(); server.Scheduler.Run(); }
        Assert.Empty(moved); // at rest: no more move events
    }

    // -- Ticking: a tickable block entity is ticked through World.Tick -> Chunk.Tick -----
    [Fact]
    public void TickableBlockEntityIsTickedByItsChunk() {
        var world = new World("betick", new FlatChunkGenerator());
        var pos = new Vector3i(3, 6, 3);
        var counter = new CountingBlockEntity(world, pos);
        world.SetBlockEntity(counter);

        world.Tick();
        world.Tick();
        Assert.Equal(2, counter.Ticks);

        // Removing it stops the ticks (and a non-ticking block entity never counted to begin with).
        world.RemoveBlockEntity(pos);
        world.Tick();
        Assert.Equal(2, counter.Ticks);
    }

    sealed class CountingBlockEntity : BlockEntity, ITickable {
        public int Ticks;
        public CountingBlockEntity(World world, Vector3i pos) : base(world, pos, VanillaMod.Chest) { }
        public void Tick() => Ticks++;
    }

    // -- Helpers -------------------------------------------------------------
    // A minimal server (capture transport) for tests that only need its EventBus/Scheduler. Any worlds passed
    // are registered so the Server ctor wires their Events + entity-id allocator.
    static Server BareServer(params (string name, World world)[] worlds) {
        var dict = new ConcurrentDictionary<string, World>();
        foreach (var (name, world) in worlds) dict[name] = world;
        return new Server(new ServerContext {
            Worlds = dict, MOTD = new TextComponent("test"), MaxPlayers = 20, TicksPerSecond = 20,
        }, new CaptureNetServer(new ProtocolJE763()));
    }

    static int DropCount(World world) =>
        world.Ecs.CountEntities(in new QueryDescription().WithAll<PickupEntityComponent>());

    static bool RoundTrip(Protocol protocol, ConnectionState state, IMessage message) {
        // EncodePayload writes [VarInt id][body] using the type's registered codec.
        var payload = protocol.EncodePayload(message);
        using var ms = new MinecraftStream(new MemoryStream(payload, writable: false));
        int id = ms.ReadVarInt();
        var codec = protocol.CodecFor(state, PacketDirection.Serverbound, id);
        return codec is not null && codec.Decode(ms).Equals(message);
    }

    // -- Command parse cache: a player's parses are invalidated when their .Requires inputs change --
    [Fact]
    public void CommandParseCacheInvalidatesPerPlayer() {
        var server = new Server(new ServerContext { Worlds = new ConcurrentDictionary<string, World> { ["overworld"] = new World("overworld", new FlatChunkGenerator()) },
            MOTD = new TextComponent("t"), MaxPlayers = 8, TicksPerSecond = 20,
        }, new CaptureNetServer(new ProtocolJE763()));
        var dispatcher = server.CommandDispatcher; // the dispatcher now needs its Server (cache scales with player count)
        bool allow = false; // stand-in for a permission / world-gate that .Requires depends on
        var replies = new List<string>();
        var sender = new CaptureSender(replies);
        var client = new CaptureNetClient(7, new ProtocolJE763());

        dispatcher.Register(l => l.Literal("secret").Requires(_ => allow)
            .Executes(c => { c.Source.Reply("ok"); return 1; }));

        // Denied: the literal is pruned, so it parses as unknown - and that parse is cached for this player.
        _ = dispatcher.ExecuteAsync(sender, "secret", client); // synchronous-bodied; completes before returning
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler
        Assert.DoesNotContain("ok", replies);

        // Flipping the gate alone changes nothing: the cached (pruned) parse is re-executed verbatim.
        allow = true;
        _ = dispatcher.ExecuteAsync(sender, "secret", client); // synchronous-bodied; completes before returning
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler
        Assert.DoesNotContain("ok", replies);

        // Invalidating the player re-keys the cache, so the next run re-parses against the new gate state.
        dispatcher.Invalidate(client.Id);
        _ = dispatcher.ExecuteAsync(sender, "secret", client); // synchronous-bodied; completes before returning
        server.Scheduler.Run(); // ExecuteAsync now defers brig.Execute to the scheduler
        Assert.Contains("ok", replies);
    }

    [Fact]
    public void WorldCommandSuggestsExistingWorldsFromServer() {
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
            ["nether"] = new World("nether", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds,
            MOTD = new TextComponent("t"), MaxPlayers = 8, TicksPerSecond = 20,
        }, new CaptureNetServer(new ProtocolJE763()));
        server.CommandDispatcher.RegisterWorld();
        // A player source (client != null) so .Requires(IsPlayer) passes - same as HandleSuggestions builds.
        var client = new CaptureNetClient(1, new ProtocolJE763());
        var source = new SenderContext(new CaptureSender(new()), server.CommandDispatcher, client);
        var brig = server.CommandDispatcher.Brigadier;

        var all = brig.GetCompletionSuggestions(brig.Parse("world ", source)).GetAwaiter().GetResult()
            .List.Select(s => s.Text).ToArray();
        Assert.Contains("overworld", all);
        Assert.Contains("nether", all);
    }

    // -- Regression: the real client sends the leading '/' in the suggestion request (SharpTester didn't) --
    // The dispatcher must skip it for parsing yet keep the range in the client's coordinates, or ask_server
    // value suggestions (player/world) silently return nothing while client-side literals still work.
    [Fact]
    public void SuggestStripsLeadingSlashAndKeepsClientRange() {
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds,
            MOTD = new TextComponent("t"), MaxPlayers = 8, TicksPerSecond = 20,
        }, new CaptureNetServer(new ProtocolJE763()));
        server.CommandDispatcher.RegisterWorld();
        var client = new CaptureNetClient(1, new ProtocolJE763());
        var sender = new CaptureSender(new());

        // What the vanilla client actually sends for "/world " + Tab: the whole input, slash included.
        var (start, length, matches) = server.CommandDispatcher.Suggest(sender, "/world ", client);
        Assert.Contains("overworld", matches);             // values come back (the bug: they didn't)
        Assert.Equal("/world ".Length, start);             // range start is at the arg in the client's input (index 7)
        Assert.Equal(0, length);

        // And a partial with the slash: "/world over" -> replaces "over" at index 7, length 4.
        var (pStart, pLength, pMatches) = server.CommandDispatcher.Suggest(sender, "/world over", client);
        Assert.Contains("overworld", pMatches);
        Assert.Equal(7, pStart);
        Assert.Equal(4, pLength);
    }

    // -- Declare Commands BYTES: the world/tp value args must reach the client flagged ask_server --
    // (in-process suggestion tests use the server's dispatcher; this is the only check on the actual wire
    //  graph the client rebuilds its tree from - the gap behind "shows <arg> placeholder but no values".)
    [Fact]
    public void DeclareCommandsFlagsValueArgsAsAskServer() {
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext { Worlds = worlds,
            MOTD = new TextComponent("t"), MaxPlayers = 8, TicksPerSecond = 20,
        }, new CaptureNetServer(new ProtocolJE763()));
        server.CommandDispatcher.RegisterWorld().RegisterTp();
        var client = new CaptureNetClient(1, new ProtocolJE763());
        var source = new SenderContext(new CaptureSender(new()), server.CommandDispatcher, client);

        using var ms = new System.IO.MemoryStream();
        var w = new MinecraftStream(ms, leaveOpen: true) { Types = new TypeMapper(typeof(ProtocolJE763)) };
        CommandTreeSerializer.Write(w, server.CommandDispatcher.Brigadier.GetRoot(), source);

        ms.Position = 0;
        var nodes = ParseDeclareCommands(new MinecraftStream(ms, leaveOpen: true));

        var worldArg = nodes.Single(n => n.Name == "name");
        Assert.True(worldArg.HasCustomSuggestions, "world <name> must be flagged has_custom_suggestions on the wire");
        Assert.Equal("minecraft:ask_server", worldArg.SuggestionType);
        var playerArg = nodes.Single(n => n.Name == "player");
        Assert.True(playerArg.HasCustomSuggestions, "tp <player> must be flagged has_custom_suggestions on the wire");
        Assert.Equal("minecraft:ask_server", playerArg.SuggestionType);
    }

    sealed record ParsedNode(int Type, string? Name, bool HasCustomSuggestions, string? SuggestionType);

    // Parses the 1.20.1 Commands node graph the way a client does, so the test sees exactly what was sent.
    static List<ParsedNode> ParseDeclareCommands(MinecraftStream s) {
        int count = s.ReadVarInt();
        var result = new List<ParsedNode>(count);
        for (int i = 0; i < count; i++) {
            byte flags = s.ReadUByte();
            int type = flags & 0x03;
            bool hasRedirect = (flags & 0x08) != 0;
            bool hasSuggestions = (flags & 0x10) != 0;
            int children = s.ReadVarInt();
            for (int c = 0; c < children; c++) s.ReadVarInt();
            if (hasRedirect) s.ReadVarInt();
            string? name = null, suggestionType = null;
            if (type == 1) {
                name = s.ReadString();
            } else if (type == 2) {
                name = s.ReadString();
                SkipParser(s);
                if (hasSuggestions) suggestionType = s.ReadString();
            }
            result.Add(new ParsedNode(type, name, hasSuggestions, suggestionType));
        }
        return result;
    }

    static void SkipParser(MinecraftStream s) {
        int parser = s.ReadVarInt();
        switch (parser) {
            case 0: break;                                 // brigadier:bool
            case 1: SkipBounds(s, () => s.ReadFloat()); break;
            case 2: SkipBounds(s, () => s.ReadDouble()); break;
            case 3: SkipBounds(s, () => s.ReadInt()); break;
            case 4: SkipBounds(s, () => s.ReadLong()); break;
            case 5: s.ReadVarInt(); break;                 // brigadier:string mode
            default: throw new InvalidOperationException($"unexpected parser id {parser}");
        }
    }

    static void SkipBounds(MinecraftStream s, Action readBound) {
        byte f = s.ReadUByte();
        if ((f & 0x01) != 0) readBound();
        if ((f & 0x02) != 0) readBound();
    }

    sealed class CaptureSender : ISender {
        readonly List<string> messages;
        public CaptureSender(List<string> messages) => this.messages = messages;
        public string Name => "test";
        public void ReceiveMessage(ChatComponent message) =>
            messages.Add(message is TextComponent t ? t.Text : message.ToString());
    }

    // -- In-memory transport doubles -----------------------------------------
    sealed class CaptureNetClient : NetClient {
        public readonly List<IMessage> Sent = new();
        public CaptureNetClient(ulong id, Protocol protocol) : base(id, protocol) { }
        public override void Send(IMessage message) => Sent.Add(message);
        public override void Send(CachedPacket packet) => Sent.Add(packet.Message);
        public override void Disconnect() { }
    }

    sealed class CaptureNetServer : INetServer {
        public readonly List<IMessage> Broadcasts = new();
        readonly List<NetClient> clients = new();
        public ProtocolRegistry Registry { get; }
        public Protocol Protocol => Registry.Default;
        public CaptureNetServer(Protocol protocol) => Registry = new ProtocolRegistry(protocol);
        public void Register(NetClient client) => clients.Add(client);
        public void Start() { }
        public void Stop() { }
        public bool TryGetClient(ulong id, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out NetClient client) {
            client = clients.FirstOrDefault(c => c.Id == id);
            return client is not null;
        }
        public void Send(ulong client, IMessage message) {
            foreach (var c in clients) if (c.Id == client) c.Send(message);
        }
        public void Broadcast(IMessage message, Func<NetClient, bool>? predicate = null) {
            Broadcasts.Add(message);
            foreach (var c in clients) if (predicate is null || predicate(c)) c.Send(message);
        }
    }
}

// Test-only event hierarchy for the polymorphic-dispatch test (a ZombieDamage IS-A DamageEvent
// and IS-A IAudited).
interface IAudited;
record DamageEvent : IAudited;
sealed record ZombieDamage(int Amount) : DamageEvent;

// Content lookups: the old static BlockRegistry/ItemRegistry.FromName is gone; resolve by full id through
// the per-type Registry, defaulting a bare path to the minecraft namespace (returns null if unregistered).
static class TestContent {
    public static BlockType? FindBlock(string name) =>
        BlockType.TryFromPath(name.Contains(':') ? name : "minecraft:" + name, out var b) ? b : null;
    public static ItemType? FindItem(string name) =>
        ItemType.TryFromPath(name.Contains(':') ? name : "minecraft:" + name, out var i) ? i : null;
}

// Drives the per-world INetworkSystem phases directly, standing in for the old Server.AnnounceSystems/
// FlushSystems helpers that Server.Tick now inlines (announce -> world tick -> flush).
static class ServerTestExtensions {
    public static void AnnounceSystems(this Server s) {
        foreach (var w in s.Worlds.Values)
            foreach (var sys in w.NetworkSystems) sys.Announce(s);
    }
    public static void FlushSystems(this Server s) {
        foreach (var w in s.Worlds.Values)
            foreach (var sys in w.NetworkSystems) sys.Flush(s);
    }
}
