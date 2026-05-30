namespace SharpMinerals.Chat;
public class TextComponent : Component {
    public string Text;
    public TextComponent(string text) {
        Text = text;
    }
    public TextComponent SetText(string text) { Text = text; return this; }
}
