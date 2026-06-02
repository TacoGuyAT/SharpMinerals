namespace SharpMinerals.Chat;
public sealed class ScoreComponent : ChatComponent<ScoreComponent> {
    public new Score Score;
    public ScoreComponent(Score score) {
        Score = score;
    }
    public ScoreComponent SetScore(Score score) { Score = score; return this; }
}

public struct Score {
    public string Name;
    public string Objective;
    public Score(string name, string objective) {
        Name = name;
        Objective = objective;
    }
}
