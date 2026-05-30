namespace SharpMinerals.Commands;

/// <summary>An <see cref="ICommand"/> from a delegate — for quickly composing leaf subcommands.</summary>
public sealed class LambdaCommand : ICommand {
    readonly Func<CommandContext, Task> action;

    public string Name { get; }
    public string Description { get; }
    public string Usage { get; }

    public LambdaCommand(string name, string description, string usage, Func<CommandContext, Task> action) {
        Name = name;
        Description = description;
        Usage = usage;
        this.action = action;
    }

    public Task ExecuteAsync(CommandContext context) => action(context);
}
