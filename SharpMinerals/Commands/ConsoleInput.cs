using Microsoft.Extensions.Logging;
using SharpMinerals.Chat;

namespace SharpMinerals.Commands;

/// <summary>
/// The server console as both a command/chat source and an <see cref="IChatSender"/>:
/// reads lines from stdin and submits them to the dispatcher as itself (so <c>/cmd</c>
/// runs a command and plain text broadcasts as chat); output is written to the log.
/// </summary>
public sealed class ConsoleInput : IChatSender {
    static readonly ILogger Log = Logging.For("Console");

    readonly CommandDispatcher dispatcher;

    public ConsoleInput(CommandDispatcher dispatcher) => this.dispatcher = dispatcher;

    public string Name => "Console";

    public void SendMessage(ChatComponent message) =>
        Log.LogInformation("{Message}", message is TextComponent t ? t.Text : message.ToString());

    public void Start() => _ = RunAsync();

    async Task RunAsync() {
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null) {
            try {
                await dispatcher.HandleAsync(this, line);
            } catch (Exception ex) {
                Log.LogWarning("command error: {Message}", ex.Message);
            }
        }
    }
}
