namespace MemoAna.Game.Abstract.Services;

using MemoAna.Game.Core;
using MemoAna.Game.Enums;
using MemoAna.Game.Models;

public interface IAIService
{
    bool IsPlaying { get; }
    int RememberedCardCount { get; }
    int VisualRevealDelayMs { get; }
    void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards);
    void ObserveCard(int position, MemoryCard card);
    Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default);
    void CancelPendingTurn();
    void Clear();
}
