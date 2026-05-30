namespace SharpMinerals.Chat;
public class SelectorComponent : ChatComponent {
    public string Selector;
    public ChatComponent? Separator;
    public SelectorComponent(string selector) {
        Selector = selector;
    }
    public SelectorComponent SetSelector(string selector) { Selector = selector; return this; }
    public SelectorComponent SetSeparator(ChatComponent separator) { Separator = separator; return this; }
}
