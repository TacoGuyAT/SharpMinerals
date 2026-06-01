using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMinerals.Chat;

/// <summary>
/// A Minecraft text component (the JSON chat format). The concrete kind — text, translatable, score,
/// selector, keybind — is determined by which content field is present, not by a type discriminator, so
/// (de)serialization is driven by <see cref="ChatComponentConverter"/> rather than STJ's built-in
/// polymorphism. This non-generic base holds the shared style/structure data and the serialization glue;
/// the fluent setters live on <see cref="ChatComponent{TSelf}"/> so they can return the concrete type.
///
/// The converter is registered only in <see cref="SerializerOptions"/> (not via a type attribute): its
/// default <c>CanConvert</c> matches the base type exactly, so once it has dispatched to a concrete type
/// that type (de)serializes on STJ's default path — which is what stops the dispatch from recursing.
/// </summary>
public abstract class ChatComponent {
    internal static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Components model their data as public fields, which STJ skips unless told.
        IncludeFields = true,
        // Omit default/empty fields (null color, false styles) so a component
        // serializes to the minimal JSON the client expects.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new ChatComponentConverter() }
    };

    public static ChatComponent FromJson(string json) =>
        JsonSerializer.Deserialize<ChatComponent>(json, SerializerOptions) ?? throw new JsonException();

    // Fluent construction entry points, e.g.
    //   ChatComponent.Text("<").AddExtra(ChatComponent.Text("Server").SetColor(TextColor.DarkPurple), $"> {msg}")
    // Each returns the concrete type so its type-specific setters (and the inherited style setters, which
    // preserve the concrete type) chain naturally. They live here — on the one uniquely-named chat type —
    // rather than in a `Component(s)` helper, which would clash with Arch's Component and the
    // SharpMinerals.Components namespace that most server code imports.
    public static TextComponent Empty() => new("");
    public static TextComponent Text(string text) => new(text);
    public static TranslatableComponent Translate(string key) => new(key);
    public static TranslatableComponent Translate(string key, params ChatComponent[] with) => new TranslatableComponent(key).AddWith(with);
    public static KeybindComponent Keybind(string key) => new(key);
    public static SelectorComponent Selector(string selector) => new(selector);
    public static ScoreComponent Score(string name, string objective) => new(new Score(name, objective));

    // The converter dispatches on the runtime type, so a base-typed component (or one nested in Extra)
    // still serializes its subclass fields.
    public override string ToString() => JsonSerializer.Serialize<ChatComponent>(this, SerializerOptions);

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
/// Fluent style/structure setters that return the concrete component type, so chains like
/// <c>Component.Text("hi").SetColor(TextColor.Red).SetBold()</c> keep their type-specific methods
/// available. Each concrete component closes the generic over itself (CRTP).
/// </summary>
public abstract class ChatComponent<TSelf> : ChatComponent where TSelf : ChatComponent<TSelf> {
    private TSelf Self => (TSelf)this;

    public TSelf AddExtra(params ChatComponent[] components) {
        (Extra ??= new()).AddRange(components);
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
