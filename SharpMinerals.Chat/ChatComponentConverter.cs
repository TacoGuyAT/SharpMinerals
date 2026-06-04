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

        // ...and an array, whose first element is the component and the rest are appended to its Extra.
        if (root.ValueKind == JsonValueKind.Array) {
            ChatComponent? head = null;
            foreach (var element in root.EnumerateArray()) {
                var child = (ChatComponent)JsonSerializer.Deserialize(element, options.GetTypeInfo(typeof(ChatComponent)))!;
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

        // Resolve the concrete type's source-generated metadata from the live options. Its converter selection
        // no longer matches this (base-only) converter, so it serializes fields directly; nested components route
        // back here because their static type is still ChatComponent.
        return (ChatComponent?)JsonSerializer.Deserialize(root, options.GetTypeInfo(concrete));
    }

    public override void Write(Utf8JsonWriter writer, ChatComponent value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options.GetTypeInfo(value.GetType()));
}
