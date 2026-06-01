using Microsoft.Extensions.Logging;
using SharpMinerals.Commands;

namespace SharpMinerals.CLI;

/// <summary>
/// The console's I/O, host-side: reads lines from stdin on a background thread and submits each to the
/// dispatcher as the supplied <see cref="ServerSender"/> (input), and logs whatever that sender receives
/// (output). The sender identity itself lives in core so tests can reuse it; only this byte-level I/O is CLI.
/// </summary>
public sealed class ConsoleInput {
    static readonly ILogger Log = Logging.For("Console");

    readonly ServerSender sender;

    public ConsoleInput(ServerSender sender) {
        this.sender = sender;
        // Output side: write whatever the sender receives (command feedback, chat) to the log, rendering the
        // component's colours/styles to ANSI. It stays in the log pipeline (one ordered stream); ChatAnsi
        // yields plain text when stdout is redirected, so a captured/file log isn't polluted with escape codes.
        sender.MessageReceived += m => Log.LogInformation("{Message}", ChatAnsi.Render(m));
    }

    /// <summary>Starts reading stdin on a background thread, so it never keeps the process alive.</summary>
    public void Start() => new Thread(Run) { Name = "Console Input", IsBackground = true }.Start();

    void Run() {
        string? line;
        while ((line = Console.ReadLine()) is not null) {
            // Once the server is stopping, stop acting on input — the host is shutting down.
            if(sender.Server is not { IsRunning: true }) break;
            if(line.StartsWith('/')) {
                try {
                    sender.RunCommandAsync(line[1..]).GetAwaiter().GetResult();
                } catch(Exception ex) {
                    Log.LogWarning("command error: {Message}", ex.Message);
                }
            } else {
                sender.SendMessage(line);
            }
        }
    }
}
