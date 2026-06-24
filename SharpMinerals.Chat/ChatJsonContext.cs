using System.Text.Json.Serialization;

namespace SharpMinerals.Chat;

/// <summary>
/// Source-generated (de)serialization metadata for the chat component tree, so chat JSON works under Native AOT
/// / trimming without reflection. The options here mirror what <see cref="ChatComponent.SerializerOptions"/> once
/// set inline (snake_case names, public fields, omit defaults). The polymorphic "kind implied by content field"
/// dispatch stays in <see cref="ChatComponentConverter"/>, which resolves each concrete type's metadata from this
/// context via the live options.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    IncludeFields = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(ChatComponent))]
[JsonSerializable(typeof(TextComponent))]
[JsonSerializable(typeof(TranslatableComponent))]
[JsonSerializable(typeof(ScoreComponent))]
[JsonSerializable(typeof(SelectorComponent))]
[JsonSerializable(typeof(KeybindComponent))]
[JsonSerializable(typeof(Score))]
[JsonSerializable(typeof(List<ChatComponent>))]
public sealed partial class ChatJsonContext : JsonSerializerContext;
