namespace SharpMinerals.CLI;

/// <summary>
/// Supplies completions for a partial console input line, decoupling <see cref="ConsoleRenderer"/> from any
/// particular command system. The renderer shows the selected candidate as inline gray "ghost" text plus a
/// single-row dropdown below the line; Tab (Shift+Tab) types/cycles the candidate, Space accepts it.
/// </summary>
internal interface ISuggestionProvider {
    /// <summary>Completions for <paramref name="input"/> (the whole current line). The result's span says which
    /// part of the input the matches replace; an empty <see cref="SuggestionResult.Matches"/> means "no suggestion".</summary>
    SuggestionResult Suggest(string input);
}

/// <summary>The span of the input being completed plus the candidate replacements (Brigadier's shape: a replace
/// range plus the list of full-token candidates).</summary>
internal readonly record struct SuggestionResult(int Start, int Length, IReadOnlyList<string> Matches) {
    public static SuggestionResult None => new(0, 0, []);
}
