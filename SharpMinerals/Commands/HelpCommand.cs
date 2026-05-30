namespace SharpMinerals.Commands;

/// <summary><c>/help [command]</c> — lists commands or shows one command's usage.</summary>
public sealed class HelpCommand : ICommand {
    public string Name => "help";
    public string Description => "Lists commands, or shows one command's usage";
    public string Usage => "/help [command]";

    public Task ExecuteAsync(CommandContext ctx) {
        if (ctx.Args.Length == 0) {
            ctx.Reply("Commands:");
            foreach (var c in ctx.Dispatcher.Commands.Values.OrderBy(c => c.Name))
                ctx.Reply($"  {c.Usage} - {c.Description}");
        } else if (ctx.Dispatcher.Commands.TryGetValue(ctx.Args[0], out var cmd)) {
            ctx.Reply($"{cmd.Usage} - {cmd.Description}");
        } else {
            ctx.Reply($"Unknown command: {ctx.Args[0]}");
        }
        return Task.CompletedTask;
    }
}
