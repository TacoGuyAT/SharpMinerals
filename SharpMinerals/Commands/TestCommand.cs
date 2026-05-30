#if TEST_HARNESS
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Commands;

/// <summary>
/// <c>/test [@&lt;clientId&gt;] &lt;command&gt;</c> — forwards a command to the test-harness mod
/// over the control channel (one client with <c>@id</c>, otherwise all play clients).
/// </summary>
public sealed class TestCommand : ICommand {
    public string Name => "test";
    public string Description => "Sends a command to the test-harness client(s)";
    public string Usage => "/test [@clientId] <command>";

    public Task ExecuteAsync(CommandContext ctx) {
        var server = Server.Instance;
        if (server is null) return Task.CompletedTask;

        string rest = ctx.ArgLine;
        ulong? target = null;
        if (rest.StartsWith('@')) {
            int space = rest.IndexOf(' ');
            if (space > 1 && ulong.TryParse(rest[1..space], out var id)) {
                target = id;
                rest = rest[(space + 1)..].Trim();
            }
        }

        if (rest.Length == 0) {
            ctx.Reply($"usage: {Usage}");
            return Task.CompletedTask;
        }

        var message = new TestCommandS2C(rest);
        if (target is ulong clientId) {
            server.NetServer.Send(clientId, message);
            ctx.Reply($"-> #{clientId}: {rest}");
        } else {
            server.NetServer.Broadcast(message, c => c.State == ConnectionState.Play);
            ctx.Reply($"-> all: {rest}");
        }
        return Task.CompletedTask;
    }
}
#endif
