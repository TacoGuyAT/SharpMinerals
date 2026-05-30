namespace SharpMinerals.Chat;
public class KeybindComponent : ChatComponent {
    public string Keybind;
    public KeybindComponent(string keybind) {
        Keybind = keybind;
    }
    public KeybindComponent SetKeybind(string keybind) { Keybind = keybind; return this; }
}
