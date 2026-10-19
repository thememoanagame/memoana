namespace MemoAna.Game.EventArgs;

public record GameTickEventArgs(int RemainingSeconds)
{
    public string NextTime { get => TimeSpan.FromSeconds(RemainingSeconds).ToString(@"mm\:ss"); }
}
