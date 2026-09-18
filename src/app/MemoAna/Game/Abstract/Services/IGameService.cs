using MemoAna.Game.Core;
using MemoAna.Game.EventArgs;
using MemoAna.Game.Enums;

namespace MemoAna.Game.Abstract.Services;

public interface IGameService
{
    ObservableCollection<KeyValuePair<int, MemoryCard>> CurrentCards { get; }
    TimeSpan RemainingTime { get; }
    bool IsGameActive { get; }
    bool IsHumanInteractionBlocked { get; }
    GameMode CurrentMode { get; }
    GameTurn CurrentTurn { get; }
    int CurrentScore { get; }
    int PlayerScore { get; }
    int AIScore { get; }
    int TotalMoves { get; }
    int PlayerSuccessfulMoves { get; }
    int AISuccessfulMoves { get; }
    int PlayerMistakes { get; }
    int AIMistakes { get; }

    event EventHandler<GameStatisticsEventArgs>? GameFinished; 
    event EventHandler<GameTickEventArgs>? TimerTick;
    event EventHandler<GameCardFlippedEventArgs>? CardFlipped;
    event EventHandler<GameTurnChangedEventArgs>? TurnChanged;
    
    Task FlipCardAsync(int position, MemoryCard selectedCard); 
    Task StartGameAsync(int difficulty, string themeName, string mode = "1");
    void ForceStopTimer();
}
