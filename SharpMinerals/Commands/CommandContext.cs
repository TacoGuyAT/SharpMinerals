using SharpMinerals.Chat;

namespace SharpMinerals.Commands;

/// <summary>The context a command runs in: the sender, its arguments and the dispatcher.</summary>
public sealed class CommandContext {
    public IChatSender Sender { get; }
    /// <summary>Everything after the command name, unsplit.</summary>
    public string ArgLine { get; }
    /// <summary>Whitespace-split tokens of <see cref="ArgLine"/>.</summary>
    public string[] Args { get; }
    public CommandDispatcher Dispatcher { get; }

    public CommandContext(IChatSender sender, string argLine, CommandDispatcher dispatcher) {
        Sender = sender;
        ArgLine = argLine.Trim();
        Args = ArgLine.Length == 0 ? Array.Empty<string>() : ArgLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dispatcher = dispatcher;
    }

    /// <summary>A child context whose arguments are <paramref name="argLine"/> (for subcommands).</summary>
    public CommandContext WithArgs(string argLine) => new(Sender, argLine, Dispatcher);

    public void Reply(string text) => Sender.SendMessage(new TextComponent(text));
}
