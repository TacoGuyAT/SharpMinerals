namespace SharpMinerals.Chat;
public class TextComponent : ChatComponent {
    public string Text;
    public TextComponent(string text) {
        Text = text;
    }
    public TextComponent SetText(string text) { Text = text; return this; }
}
