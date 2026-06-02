using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMinerals.Chat;

/// <summary>
/// (De)serializes <see cref="ChatComponent"/> in Minecraft's chat-JSON shape, where the concrete kind is
/// implied by the content field rather than a type discriminator. On write it re-dispatches to the runtime
/// type so a base-typed value (e.g. an <see cref="ChatComponent.Extra"/> element) emits its subclass fields;
/// on read it picks the subclass from whichever content key is present. Handing the concrete type back to STJ
/// (whose converter selection no longer matches) is what stops the dispatch recursing.
/// </summary>
public sealed class ChatComponentConverter : JsonConverter<ChatComponent> {
    public override ChatComponent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        // Vanilla allows a bare string as shorthand for a text component.
        if (reader.TokenType == JsonTokenType.String)
            return new TextComponent(reader.GetString()!);

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // …and an array, whose first element is the component and the rest are appended to its Extra.
        if (root.ValueKind == JsonValueKind.Array) {
            ChatComponent? head = null;
            foreach (var element in root.EnumerateArray()) {
                var child = element.Deserialize<ChatComponent>(options)!;
                if (head is null) head = child;
                else (head.Extra ??= new()).Add(child);
            }
            return head ?? new TextComponent("");
        }

        Type concrete =
            root.TryGetProperty("text", out _) ? typeof(TextComponent) :
            root.TryGetProperty("translate", out _) ? typeof(TranslatableComponent) :
            root.TryGetProperty("score", out _) ? typeof(ScoreComponent) :
            root.TryGetProperty("selector", out _) ? typeof(SelectorComponent) :
            root.TryGetProperty("keybind", out _) ? typeof(KeybindComponent) :
            // No content field (a style-only object, e.g. a parent carrying just Extra): treat as empty text.
            typeof(TextComponent);

        // Deserializing the concrete type re-enters STJ's default path; nested components route back here.
        return (ChatComponent?)root.Deserialize(concrete, options);
    }

    public override void Write(Utf8JsonWriter writer, ChatComponent value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
