namespace SharpMinerals.Chat;
public sealed class TextComponent : ChatComponent<TextComponent> {
    public string Text;
    public TextComponent(string text) {
        Text = text;
    }
    public TextComponent SetText(string text) { Text = text; return this; }

    // A plain string is the most common component, so let one stand in wherever a component is expected
    // (e.g. AddExtra("..."), Component arguments). Mirrors vanilla's bare-string shorthand.
    public static implicit operator TextComponent(string text) => new(text);
}
