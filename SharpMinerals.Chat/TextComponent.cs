namespace SharpMinerals.Chat;
public sealed class TextComponent(string text) : ChatComponent<TextComponent> {
    public new string Text { get; set; } = text;

    public TextComponent SetText(string text) { Text = text; return this; }

    // Let a plain string stand in wherever a component is expected; mirrors vanilla's bare-string shorthand.
    public static implicit operator TextComponent(string text) => new(text);
}
