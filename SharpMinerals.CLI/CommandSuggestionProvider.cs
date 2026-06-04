using SharpMinerals.Commands;

namespace SharpMinerals.CLI;

/// <summary>
/// <see cref="ISuggestionProvider"/> backed by the Brigadier command tree: forwards to
/// <see cref="CommandDispatcher.Suggest"/> for lines that look like a command (start with '/'), so console
/// completion mirrors what a connected client gets. Chat lines (no leading '/') get no suggestions.
/// </summary>
internal sealed class CommandSuggestionProvider(CommandDispatcher dispatcher, ISender sender) : ISuggestionProvider {
    public SuggestionResult Suggest(string input) {
        if (!input.StartsWith('/')) return SuggestionResult.None; // only commands, not chat
        var (start, length, matches) = dispatcher.Suggest(sender, input);
        return new SuggestionResult(start, length, matches);
    }
}
