namespace SharpMinerals.Commands;

/// <summary>A server command. Composes with others via <see cref="CompositeCommand"/>.</summary>
public interface ICommand {
    string Name { get; }
    string Description { get; }
    /// <summary>One-line usage, e.g. <c>/run &lt;file&gt;</c>.</summary>
    string Usage { get; }
    Task ExecuteAsync(CommandContext context);
}
