namespace SharpMinerals.Chat;
public sealed class SelectorComponent : ChatComponent<SelectorComponent> {
    public new string Selector;
    public ChatComponent? Separator;
    public SelectorComponent(string selector) {
        Selector = selector;
    }
    public SelectorComponent SetSelector(string selector) { Selector = selector; return this; }
    public SelectorComponent SetSeparator(ChatComponent separator) { Separator = separator; return this; }
}
