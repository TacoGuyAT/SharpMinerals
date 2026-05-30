using SharpMinerals.Chat;

namespace SharpMinerals.Commands;

/// <summary>
/// <c>/run &lt;file&gt;</c> — runs the commands in a file (one per line) on its own async
/// task, so <c>/timeout</c> can pace it without blocking anything else.
/// </summary>
public sealed class RunCommand : ICommand {
    public string Name => "run";
    public string Description => "Runs commands from a file";
    public string Usage => "/run <file>";

    public Task ExecuteAsync(CommandContext ctx) {
        if (ctx.Args.Length < 1) {
            ctx.Reply($"usage: {Usage}");
            return Task.CompletedTask;
        }

        string file = ctx.ArgLine;
        var sender = ctx.Sender;
        var dispatcher = ctx.Dispatcher;
        ctx.Reply($"running {file}");

        _ = Task.Run(async () => {
            try {
                foreach (var line in await File.ReadAllLinesAsync(file))
                    await dispatcher.HandleAsync(sender, line);
            } catch (Exception ex) {
                sender.SendMessage(new TextComponent($"run error: {ex.Message}"));
            }
        });
        return Task.CompletedTask;
    }
}
