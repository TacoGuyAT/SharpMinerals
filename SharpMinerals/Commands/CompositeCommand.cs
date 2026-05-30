namespace SharpMinerals.Commands;

/// <summary>
/// A command built by composing child commands. The first argument selects a
/// subcommand; the remainder becomes its arguments (so subcommands can themselves be
/// composite, nesting arbitrarily). With no/unknown argument it prints its usage.
/// </summary>
public abstract class CompositeCommand : ICommand {
    readonly Dictionary<string, ICommand> subcommands = new(StringComparer.OrdinalIgnoreCase);

    public abstract string Name { get; }
    public virtual string Description => "";
    public virtual string Usage => $"/{Name} <{string.Join('|', subcommands.Keys)}>";

    public IReadOnlyDictionary<string, ICommand> Subcommands => subcommands;

    protected void Add(ICommand command) => subcommands[command.Name] = command;

    public Task ExecuteAsync(CommandContext context) {
        if(context.Args.Length == 0) {
            ReplyUsage(context);
            return Task.CompletedTask;
        }

        string sub = context.Args[0];
        if(subcommands.TryGetValue(sub, out var command)) {
            string rest = context.ArgLine.Length > sub.Length ? context.ArgLine[sub.Length..].TrimStart() : "";
            return command.ExecuteAsync(context.WithArgs(rest));
        }

        context.Reply($"Unknown subcommand '{sub}'.");
        ReplyUsage(context);
        return Task.CompletedTask;
    }

    void ReplyUsage(CommandContext context) {
        context.Reply($"{Usage}");
        foreach(var c in subcommands.Values)
            context.Reply($"  {c.Usage} - {c.Description}");
    }
}
