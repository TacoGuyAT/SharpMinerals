using Brigadier.NET;
using Brigadier.NET.Builder;

namespace SharpMinerals.Commands;

/// <summary><c>/save</c> — flushes modified world chunks and online players to the persistence store. Runs on
/// the tick thread (the single writer), so serialization can't race a concurrent edit; the reply (with counts)
/// comes back on the next tick.</summary>
public static class SaveCommand {
    public static CommandDispatcher RegisterSave(this CommandDispatcher d) => d.Register(l => l
        .Literal("save").Executes(c => {
            var server = c.Source.Server;
            if (server is null) { c.Source.Reply("Server is not running."); return 0; }
            server.Events.Defer(() => {
                int chunks = server.SaveWorlds();
                int players = server.SavePlayers();
                c.Source.Reply($"Saved {chunks} chunk(s) and {players} player(s).");
            });
            return 1;
        }));
}
