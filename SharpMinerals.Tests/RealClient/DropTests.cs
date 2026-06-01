#if TEST_HARNESS
using System.Threading.Tasks;
using Xunit;

namespace SharpMinerals.Tests.RealClient;

/// <summary>
/// Real-client item-drop scenarios. Each runs in its own fresh world (the fixture switches the client in and
/// unloads the previous world), so a test seeing 0 items at the start is itself the proof that per-test world
/// isolation + unload works — the prior test's dropped item is gone.
/// </summary>
[Collection(RealClientCollection.Name)]
[TestCaseOrderer(OrderedTestCaseOrderer.Type, OrderedTestCaseOrderer.Assembly)]
public sealed class DropTests {
    readonly RealClientFixture f;
    public DropTests(RealClientFixture f) => this.f = f;

    [RealClientFact, Order(1)]
    public async Task DroppingOne_SpawnsAnItemTheClientSees() {
        await f.EnterFreshWorld("test_drop_one");
        Assert.Equal(0, await f.CountItems());
        await f.Send("drop");
        await Task.Delay(2000); // server spawns + announces, client renders
        Assert.Equal(1, await f.CountItems());
    }

    [RealClientFact, Order(2)]
    public async Task DroppingAStack_SpawnsOneItem_InAFreshWorld() {
        await f.EnterFreshWorld("test_drop_stack");
        Assert.Equal(0, await f.CountItems()); // fresh world ⇒ the previous test's item is gone (isolation)
        await f.Send("drop all");
        await Task.Delay(2000);
        Assert.Equal(1, await f.CountItems());
    }
}
#endif
