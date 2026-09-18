using MemoAna.Common.Entities;
using MemoAna.Game.Enums;
using MemoAna.Game.EventArgs;

namespace MemoAna.Game.Entities;

public sealed class GameStatisticsEntity : EntityBase
{
    public string ThemeName { get; set; } = string.Empty;
    public GameDifficulty Difficulty { get; set; }
    public DateTime PlayedAt { get; set; }
    public bool IsVictory { get; set; }

    public int RemainingSeconds { get; set; }
    public int TotalMoves { get; set; }
    public int SuccessfulMoves { get; set; }
    public int Mistakes { get; set; }
    public int FinalScore { get; set; }

    public GameStatisticsEntity(string id) : base(id) { }

    public GameStatisticsEntity() : base(Guid.CreateVersion7().ToString())
    {
        PlayedAt = DateTime.UtcNow;
    }

    public GameStatisticsEventArgs ToEventArgs(
        int playerScore = -1,
        int aiScore = 0,
        int aiFinalScore = 0,
        int aiSuccessfulMoves = 0,
        int aiMistakes = 0)
        => new((this is GameStatisticsEntity gs ? gs.ThemeName : throw new ArgumentNullException(nameof(gs))), gs.Difficulty, DateTime.FromFileTimeUtc(gs.PlayedAt.ToFileTime()), gs.IsVictory, gs.RemainingSeconds, gs.TotalMoves, gs.SuccessfulMoves, gs.Mistakes, gs.FinalScore, playerScore < 0 ? gs.FinalScore : playerScore, aiScore, aiFinalScore, aiSuccessfulMoves, aiMistakes);
}
