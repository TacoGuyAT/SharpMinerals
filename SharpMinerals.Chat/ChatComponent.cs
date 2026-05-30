using System.Text.Json;

namespace SharpMinerals.Chat;
public abstract class ChatComponent {
    internal static JsonSerializerOptions SerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Components model their data as public fields, which STJ skips unless told.
        IncludeFields = true,
        // Omit default/empty fields (null color, false styles) so a component
        // serializes to the minimal JSON the client expects.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
    };
    public static ChatComponent FromJson(string json) => JsonSerializer.Deserialize<ChatComponent>(json, SerializerOptions) ?? throw new JsonException();
    // Serialize against the runtime type so subclass fields (e.g. TextComponent.Text) are included.
    public override string ToString() => JsonSerializer.Serialize(this, GetType(), SerializerOptions);

    public List<ChatComponent>? Extra;
    public string? Color;
    public string? Font;
    public bool Bold = false;
    public bool Italic = false;
    public bool Underline = false;
    public bool Strikethrough = false;
    public bool Obfuscated = false;
    public ChatComponent AddExtra(params ChatComponent[] component) {
        if(Extra == null)
            Extra = new List<ChatComponent>();
        Extra.AddRange(component);
        return this;
    }
    public ChatComponent SetColor(TextColor color) { Color = color.Text(); return this; }
    public ChatComponent SetColor(string color) { Color = color; return this; }
    public ChatComponent SetFont(string font) { Font = font; return this; }
    public ChatComponent SetBold(bool bold) { Bold = bold; return this; }
    public ChatComponent SetItalic(bool italic) { Italic = italic; return this; }
    public ChatComponent SetUnderline(bool underline) { Underline = underline; return this; }
    public ChatComponent SetStrikethrough(bool strikethrough) { Strikethrough = strikethrough; return this; }
    public ChatComponent SetObfuscated(bool obfuscated) { Obfuscated = obfuscated; return this; }
}
