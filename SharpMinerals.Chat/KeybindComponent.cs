namespace SharpMinerals.Chat;
public sealed class KeybindComponent : ChatComponent<KeybindComponent> {
    public new string Keybind;
    public KeybindComponent(string keybind) {
        Keybind = keybind;
    }
    public KeybindComponent SetKeybind(string keybind) { Keybind = keybind; return this; }
}
