using Microsoft.Extensions.Logging;
using SharpMinerals.Chat;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Commands;

/// <summary>
/// Owns all chat. <see cref="HandleAsync"/> is the single entry point for any text a
/// sender submits: lines starting with <c>/</c> run as commands, everything else is
/// broadcast as chat. This is where the console, players and entities meet.
/// </summary>
public sealed class CommandDispatcher {
    static readonly ILogger Log = Logging.For("Chat");

    readonly Dictionary<string, ICommand> commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Commands => commands;

    public CommandDispatcher Register(ICommand command) {
        commands[command.Name] = command;
        return this;
    }

    /// <summary>Routes a sender's input: <c>/command</c> or plain chat.</summary>
    public async Task HandleAsync(IChatSender sender, string input) {
        input = input?.Trim() ?? "";
        if (input.Length == 0)
            return;

        if (input.StartsWith('/'))
            await ExecuteAsync(sender, input[1..]);
        else
            BroadcastChat(sender, input);
    }

    /// <summary>Runs a command line (no leading slash).</summary>
    public async Task ExecuteAsync(IChatSender sender, string commandLine) {
        commandLine = commandLine.Trim();
        if (commandLine.Length == 0)
            return;

        int space = commandLine.IndexOf(' ');
        string name = space < 0 ? commandLine : commandLine[..space];
        string argLine = space < 0 ? "" : commandLine[(space + 1)..];

        if (!commands.TryGetValue(name, out var command)) {
            sender.SendMessage(new TextComponent($"Unknown command: {name}. Try /help"));
            return;
        }

        try {
            await command.ExecuteAsync(new CommandContext(sender, argLine, this));
        } catch (Exception ex) {
            sender.SendMessage(new TextComponent($"Error: {ex.Message}"));
            Log.LogWarning(ex, "command '{Command}' failed", name);
        }
    }

    void BroadcastChat(IChatSender sender, string message) {
        var component = new TextComponent($"<{sender.Name}> {message}");
        Server.Instance?.NetServer.Broadcast(new SystemChatMessageS2C(component, Overlay: false),
            c => c.State == ConnectionState.Play);
        Log.LogInformation("{Message}", component.Text);
    }
}
