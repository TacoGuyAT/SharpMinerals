using Brigadier.NET;
using Brigadier.NET.Builder;
using Microsoft.Extensions.Logging;
using SharpMinerals.Modding;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;


namespace SharpMinerals.TestMod;

/// <summary>
/// The test-harness mod: registers <c>/test [@&lt;clientId&gt;] &lt;command&gt;</c>, which forwards a command to
/// the SharpTester client mod over the control channel (one client with <c>@id</c>, else all play clients).
/// Was a hard-wired <c>#if TEST_HARNESS</c> command in core; now it's a mod loaded only in test/debug, which
/// also doubles as a worked example of a command-adding mod built against the public API.
/// </summary>
[ModInfo("sharpminerals_test", "1.0.0", ["__tacoguy"])]
public sealed class TestHarnessMod : Mod {
    public override void OnServerStarted(Server server) {
        server.CommandDispatcher.Register(l => l
            .Literal("test")
            .Then(a => a.Argument("args", Arguments.GreedyString()).Executes(c => {
                if (c.Source.Server is not { } srv) return 0;

                string rest = Arguments.GetString(c, "args");
                ulong? target = null;
                if (rest.StartsWith('@')) {
                    int space = rest.IndexOf(' ');
                    if (space > 1 && ulong.TryParse(rest[1..space], out var id)) {
                        target = id;
                        rest = rest[(space + 1)..].Trim();
                    }
                }

                if (rest.Length == 0) { c.Source.Reply("usage: /test [@clientId] <command>"); return 0; }

                var message = new TestCommandS2C(rest);
                if (target is ulong clientId) {
                    srv.NetServer.Send(clientId, message);
                    c.Source.Reply($"-> #{clientId}: {rest}");
                } else {
                    srv.NetServer.Broadcast(message, conn => conn.State == ConnectionState.Play);
                    c.Source.Reply($"-> all: {rest}");
                }
                return 1;
            })));

        Logger.LogInformation("Registered /test command.");
    }
}
