using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Core;
using MemoAna.Game.Enums;
using MemoAna.Game.Models;

namespace MemoAna.Game.Services;

public sealed class AIService(IRandomSource random) : IAIService
{
    private readonly Dictionary<int, KnownCard> memory = [];
    private IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards = [];
    private AIDifficultyOptions options = AIDifficultyOptionsFactory.For(GameDifficulty.Easy, 0);
    private CancellationTokenSource? thinkingCancellation;
    private int turn;
    private int generation;
    private int playing;

    public bool IsPlaying => Volatile.Read(ref playing) == 1;
    public int RememberedCardCount => memory.Count;
    public int VisualRevealDelayMs => options.VisualRevealDelayMs;

    /// <summary>
    /// Starts a new generation, cancels older thinking, resets observed
    /// knowledge, and stores only the current board's legal-state view.
    /// </summary>
    /// <param name="difficulty">Difficulty policy for memory and decisions.</param>
    /// <param name="cards">Current cards used for availability validation.</param>
    public void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards)
    {
        CancelPendingTurn();
        memory.Clear();
        turn = 0;
        generation++;
        this.cards = cards;
        options = AIDifficultyOptionsFactory.For(difficulty, cards.Count);
    }

    /// <summary>
    /// Records only a card that the game has already exposed through its
    /// card-flipped notification. The board collection is deliberately not
    /// consulted here for hidden card content.
    /// </summary>
    /// <param name="position">The position exposed to the player.</param>
    /// <param name="card">The card instance whose face was exposed.</param>
    public void ObserveCard(int position, MemoryCard card)
    {
        // Position numbers can be reused after restart. Reference identity is
        // therefore part of the generation boundary and prevents a delayed
        // callback from an older board from contaminating this memory.
        KeyValuePair<int, MemoryCard> current = cards.FirstOrDefault(c => c.Key == position);
        if (current.Value is null || !ReferenceEquals(current.Value, card) ||
            (!card.IsFaceUp && !card.IsMatched))
            return;

        if (card.IsMatched)
        {
            memory.Remove(position);
            return;
        }

        memory[position] = new KnownCard(card.PairId, turn);
        TrimMemory();
    }

    /// <summary>
    /// Waits for AI thinking, expires invalid observations, and returns a turn
    /// selected from available positions without reading unknown card content.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for restart, end, or disposal.</param>
    /// <returns>A legal turn, or <see langword="null"/> when no turn can continue.</returns>
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
                : [.. available.Where(c => c.Key != pair.FirstPosition &&
                    c.Key != pair.SecondPosition)];
            var first = Pick(firstCandidates.Count > 0 ? firstCandidates : available);
            var secondOptions = available.Where(c => c.Key != first.Key).ToList();
            if (pair is not null)
                secondOptions = [.. secondOptions.Where(c => c.Key != pair.FirstPosition &&
                    c.Key != pair.SecondPosition)];

            return new AITurn(first.Key,
                Pick(secondOptions.Count > 0 ? secondOptions : [.. available.Where(c => c.Key != first.Key)]).Key);
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

    /// <summary>Invalidates the active generation and cancels pending thinking.</summary>
    public void CancelPendingTurn()
    {
        generation++;
        thinkingCancellation?.Cancel();
    }

    /// <summary>Clears memory and board references after cancelling pending work.</summary>
    public void Clear()
    {
        CancelPendingTurn();
        memory.Clear();
        cards = [];
        turn = 0;
    }

    /// <summary>Selects one legal candidate through the injected random source.</summary>
    /// <param name="candidates">Candidates already filtered by game state.</param>
    /// <returns>The selected candidate.</returns>
    private KeyValuePair<int, MemoryCard> Pick(List<KeyValuePair<int, MemoryCard>> candidates) =>
        candidates[random.Next(candidates.Count)];

    /// <summary>
    /// Finds two currently available positions that share a pair identifier
    /// already present twice in the AI's observed-memory dictionary.
    /// </summary>
    /// <returns>A known pair, or <see langword="null"/> when no valid known pair exists.</returns>
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

    /// <summary>
    /// Purges memory entries that are no longer legal knowledge for this turn:
    /// unavailable cards, expired observations, and positions absent from the
    /// current generation are removed before a choice is made.
    /// </summary>
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

    /// <summary>Applies the difficulty memory capacity, retaining newest observations.</summary>
    private void TrimMemory()
    {
        foreach (int position in memory.OrderBy(x => x.Value.ObservedTurn)
                     .Skip(options.MemoryCapacity).Select(x => x.Key).ToList())
            memory.Remove(position);
    }

    private sealed record KnownCard(string PairId, int ObservedTurn);
}
