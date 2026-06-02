using System.Text.Json.Serialization;

namespace SharpMinerals.Network.Handlers;

// Server-list-ping ("status") response shape. Named types (not an anonymous object) so the payload can be
// source-generated for Native AOT / trimming. camelCase naming reproduces Minecraft's lowercase JSON keys.
internal sealed record StatusResponse(StatusVersion Version, StatusPlayers Players, StatusDescription Description);
internal sealed record StatusVersion(string Name, int Protocol);
internal sealed record StatusPlayers(int Max, int Online, StatusSample[] Sample);
internal sealed record StatusSample(string Name, string Id);
internal sealed record StatusDescription(string Text);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusResponse))]
internal sealed partial class StatusJsonContext : JsonSerializerContext;
