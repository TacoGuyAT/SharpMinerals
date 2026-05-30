using System.Text.Json;

namespace SharpMinerals.Chat;
public abstract class Component {
    internal static JsonSerializerOptions SerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Always
    };
    public static Component FromJson(string json) => JsonSerializer.Deserialize<Component>(json, SerializerOptions) ?? throw new JsonException();
    public override string ToString() => JsonSerializer.Serialize(this, SerializerOptions);

    public List<Component>? Extra;
    public string? Color;
    public string? Font;
    public bool Bold = false;
    public bool Italic = false;
    public bool Underline = false;
    public bool Strikethrough = false;
    public bool Obfuscated = false;
    public Component AddExtra(params Component[] component) {
        if(Extra == null)
            Extra = new List<Component>();
        Extra.AddRange(component);
        return this;
    }
    public Component SetColor(TextColor color) { Color = color.Text(); return this; }
    public Component SetColor(string color) { Color = color; return this; }
    public Component SetFont(string font) { Font = font; return this; }
    public Component SetBold(bool bold) { Bold = bold; return this; }
    public Component SetItalic(bool italic) { Italic = italic; return this; }
    public Component SetUnderline(bool underline) { Underline = underline; return this; }
    public Component SetStrikethrough(bool strikethrough) { Strikethrough = strikethrough; return this; }
    public Component SetObfuscated(bool obfuscated) { Obfuscated = obfuscated; return this; }
}
