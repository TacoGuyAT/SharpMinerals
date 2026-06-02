using Brigadier.NET;
using Brigadier.NET.Builder;
using SharpMinerals.Chat;

namespace SharpMinerals.Commands;

/// <summary><c>/run &lt;file&gt;</c> — runs the file's commands (one per line) as the issuing source, on a
/// background task so <c>/timeout</c> can pace it without blocking the server.</summary>
public static class RunCommand {
    public static CommandDispatcher RegisterRun(this CommandDispatcher d) => d.Register(l => l
        .Literal("run")
        .Then(a => a.Argument("file", Arguments.GreedyString()).Executes(c => {
            var file = Arguments.GetString(c, "file");
            var sender = c.Source.Sender;
            var client = c.Source.Client;
            c.Source.Reply($"running {file}");
            _ = Task.Run(async () => {
                try {
                    foreach (var line in await File.ReadAllLinesAsync(file))
                        await d.ExecuteAsync(sender, line, client);
                } catch (Exception ex) {
                    sender.ReceiveMessage(new TextComponent($"run error: {ex.Message}"));
                }
            });
            return 1;
        })));
}
