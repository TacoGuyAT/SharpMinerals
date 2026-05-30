namespace SharpMinerals.Chat;
public class SelectorComponent : Component {
    public string Selector;
    public Component? Separator;
    public SelectorComponent(string selector) {
        Selector = selector;
    }
    public SelectorComponent SetSelector(string selector) { Selector = selector; return this; }
    public SelectorComponent SetSeparator(Component separator) { Separator = separator; return this; }
}
