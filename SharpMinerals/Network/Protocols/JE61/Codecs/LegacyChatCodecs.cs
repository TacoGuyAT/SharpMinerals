using System.Globalization;
using System.Text;
using SharpMinerals.Chat;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE61.Codecs;

// 1.5.2 chat is a single string16 (no JSON components) on packet 0x03, two-way. Serverbound it decodes
// into the GENERIC chat message (so the protocol-agnostic handler routes chat vs '/command' the same as
// modern); clientbound the modern component is flattened to a §-coded string — the only form the
// pre-1.7 client understands.

/// <summary>0x03 Chat (serverbound) → generic <see cref="ChatMessageC2S"/>. The raw text keeps any leading
/// '/', so <c>SubmitChat</c>/the dispatcher treat it as a command exactly like a modern client's input.</summary>
internal sealed class LegacyChatToGenericCodec : ICodec<ChatMessageC2S> {
    public void Encode(MinecraftStream s, ChatMessageC2S m) => throw LegacySb.Outbound(nameof(ChatMessageC2S));
    public ChatMessageC2S Decode(MinecraftStream s) => new(s.ReadString16());
}

/// <summary>GENERIC <see cref="SystemChatMessageS2C"/> → legacy 0x03 Chat (flattened to a §-coded string;
/// 1.5.2 has no overlay/action-bar, so <c>Overlay</c> messages just show in chat).</summary>
internal sealed class LegacyChatS2CMapper : ICodec<SystemChatMessageS2C> {
    public void Encode(MinecraftStream s, SystemChatMessageS2C m) => s.WriteString16(LegacyText.Flatten(m.Content));
    public SystemChatMessageS2C Decode(MinecraftStream s) => throw new NotSupportedException("SystemChatMessageS2C is clientbound only.");
}

/// <summary>Flattens a modern <see cref="ChatComponent"/> tree to a 1.5.2 §-coded string. Colour + styles
/// map to § codes (a colour code resets styles in 1.5.2, so it goes first); nested <c>Extra</c> is
/// concatenated. Non-text components (translatable/keybind/…) have no legacy form and contribute nothing.</summary>
static class LegacyText {
    public static string Flatten(ChatComponent component) {
        var sb = new StringBuilder();
        Append(component, sb);
        return sb.ToString();
    }

    static void Append(ChatComponent c, StringBuilder sb) {
        if (c.Color is { } color && ColorCode(color) is { } code) sb.Append('§').Append(code);
        if (c.Obfuscated) sb.Append("§k");
        if (c.Bold) sb.Append("§l");
        if (c.Strikethrough) sb.Append("§m");
        if (c.Underline) sb.Append("§n");
        if (c.Italic) sb.Append("§o");

        if (c is TextComponent t) sb.Append(t.Text);

        if (c.Extra is { } extra)
            foreach (var child in extra) Append(child, sb);
    }

    // Vanilla colour name → 1.5.2 § colour code; a modern hex colour (#rrggbb) maps to the nearest of
    // the 16 so custom colours still show coloured rather than being dropped.
    static char? ColorCode(string name) {
        char? named = name switch {
            "black" => '0', "dark_blue" => '1', "dark_green" => '2', "dark_aqua" => '3',
            "dark_red" => '4', "dark_purple" => '5', "gold" => '6', "gray" => '7',
            "dark_gray" => '8', "blue" => '9', "green" => 'a', "aqua" => 'b',
            "red" => 'c', "light_purple" => 'd', "yellow" => 'e', "white" => 'f',
            _ => null,
        };
        if (named is not null) return named;
        if (name.Length == 7 && name[0] == '#'
            && int.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return Quantize(rgb);
        return null;
    }

    // The 16 § colours form a cube: index = (bright<<3) | (R<<2) | (G<<1) | B, each channel a bit (≥128),
    // bright when the strongest channel is near-max. So a hex colour quantizes directly (approximate, but
    // only the rare custom-colour case; our own chat uses named colours).
    static char Quantize(int rgb) {
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        int idx = (r >> 7 << 2) | (g >> 7 << 1) | (b >> 7);
        if (System.Math.Max(r, System.Math.Max(g, b)) >= 213) idx |= 0b1000; // bright variant (§8–§f)
        return "0123456789abcdef"[idx];
    }
}
