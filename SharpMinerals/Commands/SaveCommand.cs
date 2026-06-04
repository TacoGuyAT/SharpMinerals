using Brigadier.NET;
using Brigadier.NET.Builder;

namespace SharpMinerals.Commands;

/// <summary><c>/save</c> - flushes modified world chunks and online players to the store. Deferred to the
/// tick thread (the single writer) so serialization can't race a concurrent edit; the reply comes next tick.</summary>
public static class SaveCommand {
    public static CommandDispatcher RegisterSave(this CommandDispatcher d) => d.Register(l => l
        .Literal("save").Executes(ctx => {
            var server = ctx.Source.Server;
            server.Events.Defer(() => {
                int chunks = server.SaveWorlds();
                int players = server.SavePlayers();
                ctx.Source.Reply($"Saved {chunks} chunk(s) and {players} player(s).");
            });
            return 1;
        }));
}
