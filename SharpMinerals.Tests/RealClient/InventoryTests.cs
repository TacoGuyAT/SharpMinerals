#if TEST_HARNESS
using System.Threading.Tasks;
using Xunit;

namespace SharpMinerals.Tests.RealClient;

/// <summary>
/// Real-client inventory scenarios: a vanilla client performs the ACTUAL drop / inventory-click / creative
/// actions (so the client builds the real Click Container, Player Action, and Set Creative Mode Slot packets),
/// and we assert the outcome through what the client itself reports (slot/cursor/held contents, world item
/// count). This is the ground-truth replacement for the old hand-built-packet "ping-pong" unit tests, whose
/// pass/fail hinged on the server and test sharing the same (possibly wrong) interpretation of those codes.
/// <para/>
/// Each test runs in its own fresh world (the fixture disposes the previous), and seeds the exact slots it
/// needs with creative <c>set</c> - inventory persists across world switches, so tests don't assume a fresh
/// starter kit.
/// </summary>
[Collection(RealClientCollection.Name)]
[TestCaseOrderer(OrderedTestCaseOrderer.Type, OrderedTestCaseOrderer.Assembly)]
public sealed class InventoryTests {
    readonly RealClientFixture f;
    public InventoryTests(RealClientFixture f) => this.f = f;

    [RealClientFact, Order(1)]
    public async Task QDropDecrementsHeldStackAndSpawnsItem() {
        await f.EnterFreshWorld("test_inv_drop");
        await f.Send("set 36 stone 64"); // 64 stone in hotbar slot 0 (the selected/held slot)
        await Task.Delay(300);
        Assert.Equal(0, await f.CountItems());

        await f.Send("drop");            // Q (drop one) - a real Player Action packet
        await Task.Delay(1500);          // server spawns + announces, client renders
        Assert.Contains("x63", await f.Send("held"));
        Assert.Equal(1, await f.CountItems());
    }

    [RealClientFact, Order(2)]
    public async Task CreativeThrowSpawnsItem() {
        await f.EnterFreshWorld("test_inv_creative_throw");
        Assert.Equal(0, await f.CountItems());
        await f.Send("set -1 stone 1");  // Set Creative Mode Slot, slot -1 = throw into the world
        await Task.Delay(1500);
        Assert.Equal(1, await f.CountItems());
    }

    [RealClientFact, Order(3)]
    public async Task WindowZeroClickMovesHeldToHelmetThenThrows() {
        await f.EnterFreshWorld("test_inv_click");
        await f.Send("set 36 stone 64"); // hotbar 0 (window-0 slot 36)
        await f.Send("set 5 air 0");     // clear the helmet slot (window-0 slot 5) for a clean start
        await Task.Delay(300);
        Assert.Equal(0, await f.CountItems());

        await f.Send("click 0 36 0 0");  // left-click hotbar 0 -> stack onto the cursor
        await Task.Delay(400);
        Assert.Contains("stone", await f.Send("cursor"));
        Assert.Contains("air", await f.Send("slot 36"));

        await f.Send("click 0 5 0 0");   // left-click the helmet slot -> place the stack there
        await Task.Delay(400);
        Assert.Contains("stone", await f.Send("slot 5"));

        await f.Send("click 0 5 1 4");   // drop key on the helmet slot (mode 4 THROW, button 1 = whole stack)
        await Task.Delay(1500);
        Assert.Contains("air", await f.Send("slot 5"));
        Assert.Equal(1, await f.CountItems());
    }

    [RealClientFact, Order(4)]
    public async Task LeftDragDistributesCursorEvenly() {
        await f.EnterFreshWorld("test_inv_drag");
        await f.Send("set 36 stone 64"); // a full stack to drag
        await f.Send("set 9 air 0");     // clear the three target main slots
        await f.Send("set 10 air 0");
        await f.Send("set 11 air 0");
        await Task.Delay(300);

        await f.Send("click 0 36 0 0");   // pick the stack onto the cursor
        await f.Send("click 0 -999 0 5"); // start left-drag
        await f.Send("click 0 9 1 5");    // paint slots 9, 10, 11
        await f.Send("click 0 10 1 5");
        await f.Send("click 0 11 1 5");
        await f.Send("click 0 -999 2 5"); // end left-drag -> 64/3 = 21 each, remainder 1 on the cursor
        await Task.Delay(500);

        Assert.Contains("x21", await f.Send("slot 9"));
        Assert.Contains("x21", await f.Send("slot 10"));
        Assert.Contains("x21", await f.Send("slot 11"));
        Assert.Contains("x1", await f.Send("cursor"));
    }

    [RealClientFact, Order(5)]
    public async Task HotbarNumberKeySwapAndCreativeClone() {
        await f.EnterFreshWorld("test_inv_hotbar");
        await f.Send("click 0 -999 0 0"); // clear any item left on the cursor by a prior test (cursor persists)
        await f.Send("set 36 stone 64"); // hotbar 0
        await f.Send("set 37 chest 64"); // hotbar 1
        await f.Send("set 9 air 0");     // clear the swap target
        await Task.Delay(300);

        // Number-key swap (mode 2, button 0 = hotbar 0): hovering main slot 9 moves hotbar-0's stone there.
        await f.Send("click 0 9 0 2");
        await Task.Delay(400);
        Assert.Contains("stone", await f.Send("slot 9"));
        Assert.Contains("air", await f.Send("slot 36"));

        // Creative clone (mode 3, middle-click) hotbar 1 (slot 37) -> a full stack onto the cursor.
        await f.Send("click 0 37 2 3");
        await Task.Delay(400);
        var cursor = await f.Send("cursor");
        Assert.Contains("chest", cursor);
        Assert.Contains("x64", cursor);
    }
}
#endif
