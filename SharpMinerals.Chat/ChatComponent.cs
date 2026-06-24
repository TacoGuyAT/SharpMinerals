using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SharpMinerals.Chat;

/// <summary>
/// A Minecraft text component (JSON chat format). The concrete kind (text, translatable, score, selector,
/// keybind) is identified by which content field is present, so (de)serialization goes through
/// <see cref="ChatComponentConverter"/> rather than STJ polymorphism. This non-generic base holds the shared
/// style/structure data; the fluent setters live on <see cref="ChatComponent{TSelf}"/> to return the concrete
/// type.
/// </summary>
public abstract class ChatComponent {
    // Copied FROM the source-generated context's options, so it inherits the resolver AND its settings
    // (snake_case names, public fields, omit defaults) with no reflection. The polymorphic converter is layered
    // on top; it applies only to the base type (its CanConvert matches ChatComponent exactly), so a concrete
    // component still serializes its own fields.
    internal static readonly JsonSerializerOptions SerializerOptions = new(ChatJsonContext.Default.Options) {
        Converters = { new ChatComponentConverter() }
    };

    static readonly JsonTypeInfo<ChatComponent> ComponentTypeInfo =
        (JsonTypeInfo<ChatComponent>)SerializerOptions.GetTypeInfo(typeof(ChatComponent));

    public static ChatComponent FromJson(string json) =>
        JsonSerializer.Deserialize(json, ComponentTypeInfo) ?? throw new JsonException();

    // Fluent construction entry points; each returns the concrete type so its setters chain. They live on this
    // type rather than a `Component(s)` helper to avoid clashing with Arch's Component / the Components namespace.
    public static TextComponent Empty() => new("");
    public static TextComponent Text(string text) => new(text);
    public static TranslatableComponent Translate(string key) => new(key);
    public static TranslatableComponent Translate(string key, params ChatComponent[] with) => new TranslatableComponent(key).AddWith(with);
    public static KeybindComponent Keybind(string key) => new(key);
    public static SelectorComponent Selector(string selector) => new(selector);
    public static ScoreComponent Score(string name, string objective) => new(new Score(name, objective));

    // Serialized via the runtime type's metadata, so a base-typed value still emits its subclass fields.
    public override string ToString() => JsonSerializer.Serialize(this, SerializerOptions.GetTypeInfo(GetType()));

    public List<ChatComponent>? Extra;
    public string? Color;
    public string? Font;
    public bool Bold = false;
    public bool Italic = false;
    public bool Underline = false;
    public bool Strikethrough = false;
    public bool Obfuscated = false;
}

/// <summary>
/// Fluent style/structure setters returning the concrete component type (CRTP), so chains like
/// <c>Component.Text("hi").SetColor(TextColor.Red).SetBold()</c> keep their type-specific methods.
/// </summary>
public abstract class ChatComponent<TSelf> : ChatComponent where TSelf : ChatComponent<TSelf> {
    private TSelf Self => (TSelf)this;

    public TSelf With(params ChatComponent[] components) {
        (Extra ??= []).AddRange(components);
        return Self;
    }
    public TSelf SetColor(TextColor color) { Color = color.Text(); return Self; }
    public TSelf SetColor(string color) { Color = color; return Self; }
    public TSelf SetFont(string font) { Font = font; return Self; }
    public TSelf SetBold(bool bold = true) { Bold = bold; return Self; }
    public TSelf SetItalic(bool italic = true) { Italic = italic; return Self; }
    public TSelf SetUnderline(bool underline = true) { Underline = underline; return Self; }
    public TSelf SetStrikethrough(bool strikethrough = true) { Strikethrough = strikethrough; return Self; }
    public TSelf SetObfuscated(bool obfuscated = true) { Obfuscated = obfuscated; return Self; }
}
