using Brigadier.NET;
using Brigadier.NET.Builder;
using Microsoft.Extensions.Logging;
using SharpMinerals.Modding;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;


namespace SharpMinerals.TestMod;

/// <summary>
/// The test-harness mod: registers <c>/test [@&lt;clientId&gt;] &lt;command&gt;</c>, forwarding a command to the
/// SharpTester client mod over the control channel (one client with <c>@id</c>, else all play clients). Also
/// a worked example of a command-adding mod built against the public API.
/// </summary>
[ModInfo("sharpminerals_test", "1.0.0", ["__tacoguy"], TargetServerVersion = "0.1.0")]
public sealed class TestMod : Mod {
    public override void OnServerStarted(Server server) {
        server.CommandDispatcher.Register(l => l
            .Literal("test")
            .Then(x => x.Argument("args", Arguments.GreedyString()).Executes(ctx => {
                var srv = ctx.Source.Server;

                string rest = Arguments.GetString(ctx, "args");
                ulong? target = null;
                if (rest.StartsWith('@')) {
                    int space = rest.IndexOf(' ');
                    if (space > 1 && ulong.TryParse(rest[1..space], out var id)) {
                        target = id;
                        rest = rest[(space + 1)..].Trim();
                    }
                }

                if (rest.Length == 0) { ctx.Source.Reply("usage: /test [@clientId] <command>"); return 0; }

                var message = new TestCommandS2C(rest);
                if (target is ulong clientId) {
                    srv.NetServer.Send(clientId, message);
                    ctx.Source.Reply($"-> #{clientId}: {rest}");
                } else {
                    srv.NetServer.Broadcast(message, conn => conn.State == ConnectionState.Play);
                    ctx.Source.Reply($"-> all: {rest}");
                }
                return 1;
            })));

        Logger.LogInformation("Registered /test command.");
    }
}
