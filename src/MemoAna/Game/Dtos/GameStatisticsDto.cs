using MemoAna.Game.Enums;

namespace MemoAna.Game.Dtos;

public sealed record GameStatisticsDto(
    string ThemeName,
    GameDifficulty Difficulty,
    DateTime PlayedAt,
    bool IsVictory,
    int RemainingSeconds,
    int TotalMoves,
    int SuccessfulMoves,
    int Mistakes,
    int FinalScore,
    int PlayerScore = 0,
    int AIScore = 0,
    int AIFinalScore = 0,
    int AISuccessfulMoves = 0,
    int AIMistakes = 0)
{
    public static readonly GameStatisticsDto Default = new(string.Empty, (GameDifficulty)3, DateTime.Today, false, 0, 0, 0, 0, 0);
}
