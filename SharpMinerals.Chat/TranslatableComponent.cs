namespace SharpMinerals.Chat;
public class TranslatableComponent : ChatComponent {
    public string Translate;
    public string? Fallback;
    public List<ChatComponent>? With;
    public TranslatableComponent(string translate) {
        Translate = translate;
    }
    public TranslatableComponent SetTranslate(string translate) { Translate = translate; return this; }
    public TranslatableComponent SetFallback(string fallback) { Fallback = fallback; return this; }
    public TranslatableComponent AddWith(params ChatComponent[] component) {
        if(With == null)
            With = new List<ChatComponent>();
        With.AddRange(component);
        return this;
    }
}
