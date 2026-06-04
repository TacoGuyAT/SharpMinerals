#if TEST_HARNESS
using System.Threading.Tasks;
using Xunit;

namespace SharpMinerals.Tests.RealClient;

/// <summary>
/// Real-client command auto-completion: a vanilla client is the ground truth. The server sends Declare
/// Commands on join; SharpTester's <c>tree</c> reports the command graph the client built from it, and
/// <c>suggest</c> runs the client's own completion (driving the real ask_server 0x09/0x0F round-trip for
/// dynamic args) and reports the matches. Each test runs in its own fresh world (the fixture switches the
/// client in and unloads the previous), isolating the world-name suggestions.
/// <para/>
/// Note: the server trims the command line, so the empty-trailing-arg case (<c>"world "</c>) can't be sent
/// through <c>/test</c>; these tests use a partial token (e.g. <c>"world test"</c>) instead, which drives the
/// same argument-completion path.
/// </summary>
[Collection(RealClientCollection.Name)]
[TestCaseOrderer(OrderedTestCaseOrderer.Type, OrderedTestCaseOrderer.Assembly)]
public sealed class SuggestionTests {
    readonly RealClientFixture f;
    public SuggestionTests(RealClientFixture f) => this.f = f;

    [RealClientFact, Order(1)]
    public async Task ClientBuildsTheCommandTreeFromDeclareCommands() {
        await f.EnterFreshWorld("test_tree");
        var reply = await f.Send("tree"); // root command names the client parsed from Declare Commands
        foreach (var name in new[] { "help", "server", "save", "tp", "world", "test" })
            Assert.Contains(name, reply);
    }

    [RealClientFact, Order(2)]
    public async Task ServerLiteralSubcommandCompletesOnTheClient() {
        await f.EnterFreshWorld("test_tree_sub");
        // `server`'s children are literals - the client completes them locally from the tree. "server t" -> tps.
        var reply = await f.Send("suggest server t");
        Assert.Contains("tps", reply);
        Assert.Contains("range=7+1", reply); // replaces the "t" at index 7 of "server t" - the span the UI shows at
    }

    [RealClientFact, Order(3)]
    public async Task WorldArgumentSuggestsExistingWorldsViaAskServer() {
        await f.EnterFreshWorld("test_suggest_world");
        // `/world <name>` is ask_server: typing "world test" makes the client request, and the server answers
        // with its loaded world keys matching "test" - which includes this test's fresh world. Proves 0x09/0x0F.
        var reply = await f.Send("suggest world test");
        Assert.Contains("test_suggest_world", reply);
        Assert.Contains("range=6+4", reply); // replaces the "test" token at index 6 - wrong range => UI shows nothing
    }

    [RealClientFact, Order(4)]
    public async Task TpPlayerArgumentSuggestsOnlinePlayerViaAskServer() {
        await f.EnterFreshWorld("test_suggest_tp");
        // `/tp <player>` is ask_server: the only online player is this client, so a prefix of its name should
        // complete to the full name via the server round-trip.
        var prefix = f.PlayerName.Length > 0 ? f.PlayerName[..1] : "_";
        var reply = await f.Send($"suggest tp {prefix}");
        Assert.Contains(f.PlayerName, reply);
        Assert.Contains("range=3+1", reply); // replaces the 1-char prefix at index 3 of "tp X"
    }
}
#endif
