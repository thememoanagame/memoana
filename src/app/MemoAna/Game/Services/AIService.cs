using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Core;
using MemoAna.Game.Enums;
using MemoAna.Game.Models;

namespace MemoAna.Game.Services;

public sealed class AIService : IAIService
{
    private readonly IRandomSource random;
    private readonly Dictionary<int, KnownCard> memory = [];
    private IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards = [];
    private AIDifficultyOptions options = AIDifficultyOptionsFactory.For(GameDifficulty.Easy, 0);
    private CancellationTokenSource? thinkingCancellation;
    private int turn;
    private int generation;
    private int playing;

    public bool IsPlaying => Volatile.Read(ref playing) == 1;
    public int RememberedCardCount => memory.Count;

    public AIService(IRandomSource random) => this.random = random;

    public void StartGame(GameDifficulty difficulty,
        IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards)
    {
        CancelPendingTurn();
        memory.Clear();
        turn = 0;
        generation++;
        this.cards = cards;
        options = AIDifficultyOptionsFactory.For(difficulty, cards.Count);
    }

    public void ObserveCard(int position, MemoryCard card)
    {
        if (card.IsMatched)
        {
            memory.Remove(position);
            return;
        }

        memory[position] = new KnownCard(card.PairId, turn);
        TrimMemory();
    }

    public async Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref playing, 1, 0) != 0)
            return null;

        int currentGeneration = generation;
        thinkingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await Task.Delay(options.ThinkingDelayMs, thinkingCancellation.Token);
            if (currentGeneration != generation)
                return null;

            turn++;
            RemoveUnavailableCards();
            var available = cards.Where(c => !c.Value.IsFaceUp && !c.Value.IsMatched).ToList();
            if (available.Count < 2)
                return null;

            var pair = FindKnownPair();
            bool shouldPlayKnownPair = pair is not null &&
                random.NextDouble() >= options.DeliberateErrorChance;
            if (shouldPlayKnownPair)
                return pair;

            var firstCandidates = pair is null
                ? available
                : available.Where(c => c.Key != pair.FirstPosition &&
                    c.Key != pair.SecondPosition).ToList();
            var first = Pick(firstCandidates.Count > 0 ? firstCandidates : available);
            var secondOptions = available.Where(c => c.Key != first.Key).ToList();
            if (pair is not null)
                secondOptions = secondOptions.Where(c => c.Key != pair.FirstPosition &&
                    c.Key != pair.SecondPosition).ToList();

            return new AITurn(first.Key,
                Pick(secondOptions.Count > 0 ? secondOptions : available
                    .Where(c => c.Key != first.Key).ToList()).Key);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            thinkingCancellation?.Dispose();
            thinkingCancellation = null;
            Volatile.Write(ref playing, 0);
        }
    }

    public void CancelPendingTurn()
    {
        generation++;
        thinkingCancellation?.Cancel();
    }

    public void Clear()
    {
        CancelPendingTurn();
        memory.Clear();
        cards = [];
        turn = 0;
    }

    private KeyValuePair<int, MemoryCard> Pick(List<KeyValuePair<int, MemoryCard>> candidates) =>
        candidates[random.Next(candidates.Count)];

    private AITurn? FindKnownPair()
    {
        if (random.NextDouble() > options.KnownPairPriority)
            return null;

        foreach (var group in memory.GroupBy(x => x.Value.PairId))
        {
            var positions = group.Select(x => x.Key)
                .Where(position => cards.Any(c => c.Key == position &&
                    !c.Value.IsFaceUp && !c.Value.IsMatched)).Take(2).ToList();
            if (positions.Count == 2)
                return new AITurn(positions[0], positions[1]);
        }
        return null;
    }

    private void RemoveUnavailableCards()
    {
        foreach (int position in memory.Keys.ToList())
        {
            var card = cards.FirstOrDefault(c => c.Key == position).Value;
            if (card is null || card.IsMatched || card.IsFaceUp ||
                turn - memory[position].ObservedTurn > options.MemoryRetentionTurns)
                memory.Remove(position);
        }
        TrimMemory();
    }

    private void TrimMemory()
    {
        foreach (int position in memory.OrderBy(x => x.Value.ObservedTurn)
                     .Skip(options.MemoryCapacity).Select(x => x.Key).ToList())
            memory.Remove(position);
    }

    private sealed record KnownCard(string PairId, int ObservedTurn);
}
