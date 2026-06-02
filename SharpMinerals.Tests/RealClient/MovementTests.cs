#if TEST_HARNESS
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace SharpMinerals.Tests.RealClient;

/// <summary>
/// Real-client movement + chunk-streaming. Drives the SharpTester client's Baritone <c>goto</c> across chunk
/// boundaries: the client can only path forward if the server streams the terrain ahead as it moves, so a
/// player that actually arrives several chunks away proves both movement handling and chunk streaming. Runs in
/// the flat lobby world (walkable terrain). This is the baseline before the PlayerMoved→systems conversion.
/// </summary>
[Collection(RealClientCollection.Name)]
[TestCaseOrderer(OrderedTestCaseOrderer.Type, OrderedTestCaseOrderer.Assembly)]
public sealed class MovementTests {
    readonly RealClientFixture f;
    public MovementTests(RealClientFixture f) => this.f = f;

    // "pos 0.50 65.00 0.50 yaw 0.0 pitch 0.0" → the X coordinate.
    static double PosX(string reply) =>
        double.Parse(reply.Split(' ')[1], CultureInfo.InvariantCulture);

    [RealClientFact, Order(1)]
    public async Task WalksEastAcrossChunks() {
        double startX = PosX(await f.Send("pos"));

        await f.Send("goto 48 0"); // east into chunk (3,0); Baritone paths only over streamed terrain
        await Task.Delay(20000);   // ~48 blocks of flat-ground pathing

        double endX = PosX(await f.Send("pos"));
        Assert.True(endX - startX > 32,
            $"player should have walked east across chunk boundaries (start x={startX}, end x={endX})");
    }
}
#endif
