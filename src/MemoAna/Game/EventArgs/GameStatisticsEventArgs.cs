using MemoAna.Game.Enums;

namespace MemoAna.Game.EventArgs;

public sealed record GameStatisticsEventArgs(
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
    int AIMistakes = 0);
