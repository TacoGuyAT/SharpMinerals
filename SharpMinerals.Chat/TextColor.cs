namespace SharpMinerals.Chat;
public static class TextColors {
    public static string BLACK = "black";
    public static string DARK_BLUE = "dark_blue";
    public static string DARK_GREEN = "dark_green";
    public static string DARK_AQUA = "dark_aqua";
    public static string DARK_RED = "dark_red";
    public static string DARK_PURPLE = "dark_purple";
    public static string GOLD = "gold";
    public static string GRAY = "gray";
    public static string DARK_GRAY = "dark_gray";
    public static string BLUE = "blue";
    public static string GREEN = "green";
    public static string AQUA = "aqua";
    public static string RED = "red";
    public static string LIGHT_PURPLE = "light_purple";
    public static string YELLOW = "yellow";
    public static string WHITE = "white";
    public static string Text(this TextColor color) {
        return color switch {
            TextColor.Black => BLACK,
            TextColor.DarkBlue => DARK_BLUE,
            TextColor.DarkGreen => DARK_GREEN,
            TextColor.DarkAqua => DARK_AQUA,
            TextColor.DarkRed => DARK_RED,
            TextColor.DarkPurple => DARK_PURPLE,
            TextColor.Gold => GOLD,
            TextColor.Gray => GRAY,
            TextColor.DarkGray => DARK_GRAY,
            TextColor.Blue => BLUE,
            TextColor.Green => GREEN,
            TextColor.Aqua => AQUA,
            TextColor.Red => RED,
            TextColor.LightPurple => LIGHT_PURPLE,
            TextColor.Yellow => YELLOW,
            TextColor.White => WHITE,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid text color value {color}")
        };
    }
}

public enum TextColor {
    Black,
    DarkBlue,
    DarkGreen,
    DarkAqua,
    DarkRed,
    DarkPurple,
    Gold,
    Gray,
    DarkGray,
    Blue,
    Green,
    Aqua,
    Red,
    LightPurple,
    Yellow,
    White
}