namespace SharpMinerals.Chat;
public class TranslatableComponent : Component {
    public string Translate;
    public string? Fallback;
    public List<Component>? With;
    public TranslatableComponent(string translate) {
        Translate = translate;
    }
    public TranslatableComponent SetTranslate(string translate) { Translate = translate; return this; }
    public TranslatableComponent SetFallback(string fallback) { Fallback = fallback; return this; }
    public TranslatableComponent AddWith(params Component[] component) {
        if(With == null)
            With = new List<Component>();
        With.AddRange(component);
        return this;
    }
}
