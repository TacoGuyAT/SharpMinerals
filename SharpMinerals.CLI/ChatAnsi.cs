using System.Globalization;
using System.Text;
using Kokuban;
using Kokuban.AnsiEscape;
using SharpMinerals.Chat;

namespace SharpMinerals.CLI;

/// <summary>
/// Renders a <see cref="ChatComponent"/> tree to an ANSI-coloured string for the host console (via Kokuban):
/// colours/styles become SGR codes and <c>Extra</c> children inherit the parent's style. Kokuban yields plain
/// text when stdout is redirected, so this is always safe to feed into the log pipeline. Strikethrough and
/// obfuscated have no terminal equivalent and are dropped; non-text components contribute only their
/// styled <c>Extra</c>.
/// </summary>
static class ChatAnsi {
    public static string Render(ChatComponent component) {
        var sb = new StringBuilder();
        Append(component, default, sb);
        return sb.ToString();
    }

    static void Append(ChatComponent c, AnsiStyle inherited, StringBuilder sb) {
        var style = Merge(inherited, c);
        if (c is TextComponent { Text.Length: > 0 } t) sb.Append(style[t.Text]);
        if (c.Extra is { } extra)
            foreach (var child in extra) Append(child, style, sb);
    }

    // A child inherits the parent's style, layering its own non-default fields on top. A false style bit means
    // "inherit" (not "off"), so we only ever add styles, never clear them.
    static AnsiStyle Merge(AnsiStyle s, ChatComponent c) {
        if (c.Color is { } color) s = ApplyColour(s, color);
        if (c.Bold) s = s.Bold;
        if (c.Italic) s = s.Italic;
        if (c.Underline) s = s.Underline;
        return s;
    }

    // Vanilla colour name → nearest ANSI colour (dark_* to non-bright); a hex #rrggbb renders as truecolor.
    static AnsiStyle ApplyColour(AnsiStyle s, string name) => name switch {
        "black" => s.Black,
        "dark_blue" => s.Blue,
        "dark_green" => s.Green,
        "dark_aqua" => s.Cyan,
        "dark_red" => s.Red,
        "dark_purple" => s.Magenta,
        "gold" => s.Yellow,
        "gray" => s.White,
        "dark_gray" => s.BrightBlack,
        "blue" => s.BrightBlue,
        "green" => s.BrightGreen,
        "aqua" => s.BrightCyan,
        "red" => s.BrightRed,
        "light_purple" => s.BrightMagenta,
        "yellow" => s.BrightYellow,
        "white" => s.BrightWhite,
        _ when name.Length == 7 && name[0] == '#'
            && int.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
            => s.Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb),
        _ => s,
    };
}
