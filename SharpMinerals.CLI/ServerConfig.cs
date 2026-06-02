using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMinerals.CLI;

/// <summary>The minimum level the host logger emits, serialized as a lowercase string in
/// <c>server.json</c>.</summary>
enum LogLevel { Trace, Debug, Info, Warn, Error }

/// <summary>
/// Host configuration, loaded from <c>server.json</c> (defaults written on first run): listen endpoint, MOTD,
/// player/tick limits, the main world and its data directory, and the log directory. Relative paths resolve
/// against the working directory — keep them outside <c>bin/</c> so the world survives rebuilds.
/// </summary>
sealed record ServerConfig {
    public string Host { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 25565;
    public string Motd { get; init; } = "A SharpMinerals server";
    public int MaxPlayers { get; init; } = 20;
    public double Tps { get; init; } = 20.0;
    public string World { get; init; } = "overworld";
    public string DataDir { get; init; } = "world";  // RocksDB players/ + chunks/ live under here
    public string LogsDir { get; init; } = "logs";
    public LogLevel LogLevel { get; init; } = LogLevel.Info;
    public string Startup { get; init; } = "";

    static readonly JsonSerializerOptions Json = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new LogLevelJsonConverter() },
    };

    /// <summary>A loaded config plus an optional startup notice. Loading happens before logging is configured
    /// (logging is built from this config), so the notice is returned to be logged once the logger exists.</summary>
    public readonly record struct LoadResult(ServerConfig Config, string? Notice, bool NoticeIsWarning);

    /// <summary>Loads the config from <paramref name="path"/>, or writes a default template and returns defaults.</summary>
    public static LoadResult Load(string path) {
        if (File.Exists(path)) {
            try {
                var config = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Json) ?? new ServerConfig();
                return new LoadResult(config, null, false);
            } catch (Exception ex) {
                return new LoadResult(new ServerConfig(), $"failed to read {path}: {ex.Message} — using defaults", true);
            }
        }

        var defaults = new ServerConfig();
        try {
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, Json));
            return new LoadResult(defaults, $"wrote default config {path}", false);
        } catch (Exception ex) {
            return new LoadResult(defaults, $"could not write {path}: {ex.Message}", true);
        }
    }
}

/// <summary>Reads <see cref="LogLevel"/> from its config string (case-insensitive, a few aliases);
/// missing/null/unrecognized resolves to <see cref="LogLevel.Info"/>. Writes the lowercase enum name.</summary>
sealed class LogLevelJsonConverter : JsonConverter<LogLevel> {
    public override bool HandleNull => true; // null/missing → the default, not an error

    public override LogLevel Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        (reader.TokenType == JsonTokenType.String ? reader.GetString() : null)?.Trim().ToLowerInvariant() switch {
            "trace" or "verbose" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" or "information" => LogLevel.Info,
            "warn" or "warning" => LogLevel.Warn,
            "error" => LogLevel.Error,
            _ => LogLevel.Info,
        };

    public override void Write(Utf8JsonWriter writer, LogLevel value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}
