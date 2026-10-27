using System.Linq.Expressions;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Entities;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Core;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;
using MemoAna.Game.Enums;
using MemoAna.Game.EventArgs;
using MemoAna.Game.Services;
using MemoAna.Game.Models;
using Microsoft.Maui.Dispatching;
using Xunit;
using System.Diagnostics;

namespace MemoAna.UnitTests.Game;

public sealed class GameServiceTests
{
    [Fact]
    public async Task StartingIaGameBeginsOnPlayerTurn()
    {
        await using GameService game = CreateGameService();

        await game.StartGameAsync(0, "theme", "0");

        Assert.Equal(GameTurn.Player, game.CurrentTurn);
        Assert.False(game.IsHumanInteractionBlocked);
    }

    [Fact]
    public async Task HumanTurnTransitionsToAiAndReturnsToPlayer()
    {
        await using GameService game = CreateGameService();
        await game.StartGameAsync(0, "theme", "0");

        var differentCards = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();
        var turns = new List<GameTurn>();
        game.TurnChanged += (_, args) => turns.Add(args.CurrentTurn);

        await game.FlipCardAsync(differentCards[0].Key, differentCards[0].Value);
        await game.FlipCardAsync(differentCards[1].Key, differentCards[1].Value);

        Assert.Contains(GameTurn.AI, turns);
        Assert.Equal(GameTurn.Player, game.CurrentTurn);
        Assert.False(game.IsHumanInteractionBlocked);
    }

    [Fact]
    public async Task AiTurnKeepsOwnershipAndCompletesBothFlipsBeforeReturningControl()
    {
        var ai = new AtomicAiService();
        await using GameService game = CreateGameService(ai);
        var events = new List<string>();
        Task? blockedPlayerAttempt = null;

        game.TurnChanged += (_, args) => events.Add($"turn:{args.CurrentTurn}");
        game.CardFlipped += (_, args) =>
        {
            events.Add($"card:{args.MemoryCard.Position}");
            if (game.CurrentTurn == GameTurn.AI && blockedPlayerAttempt is null)
            {
                KeyValuePair<int, MemoryCard> card = game.CurrentCards
                    .First(candidate => !candidate.Value.IsFaceUp && !candidate.Value.IsMatched);
                Assert.True(game.IsHumanInteractionBlocked);
                Assert.Equal(GameTurn.AI, game.CurrentTurn);
                blockedPlayerAttempt = game.FlipCardAsync(card.Key, card.Value);
            }
        };

        await game.StartGameAsync(0, "theme", "0");
        var humanCards = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();

        await game.FlipCardAsync(humanCards[0].Key, humanCards[0].Value);
        await game.FlipCardAsync(humanCards[1].Key, humanCards[1].Value);
        await blockedPlayerAttempt!;

        Assert.Equal(GameTurn.Player, game.CurrentTurn);
        Assert.Equal(
            ["turn:AI", $"card:{ai.FirstPosition}", $"card:{ai.SecondPosition}", "turn:Player"],
            events.SkipWhile(value => value != "turn:AI").Take(4));
        Assert.DoesNotContain(game.CurrentCards, card => card.Value.IsFaceUp && card.Key != ai.FirstPosition && card.Key != ai.SecondPosition);
    }

    [Fact]
    public async Task CardObservationOccursAfterRevealNotification()
    {
        var order = new List<string>();
        await using GameService game = CreateGameService(new RecordingAIService(order));
        game.CardFlipped += (_, _) => order.Add("render-notification");

        await game.StartGameAsync(0, "theme", "0");
        MemoryCard card = game.CurrentCards[0].Value;
        await game.FlipCardAsync(1, card);

        Assert.Equal(["render-notification", "observe"], order);
    }

    [Fact]
    public async Task MismatchKeepsBothCardsVisibleBeforeHidingThem()
    {
        await using GameService game = CreateGameService();
        var states = new List<bool>();
        game.CardFlipped += (_, _) =>
        {
            states.Add(game.CurrentCards.Any(card => card.Value.IsFaceUp));
        };
        await game.StartGameAsync(0, "theme", "2");
        var differentCards = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();

        await game.FlipCardAsync(differentCards[0].Key, differentCards[0].Value);
        await game.FlipCardAsync(differentCards[1].Key, differentCards[1].Value);

        Assert.Equal([true, true, true, false], states);
        Assert.All(game.CurrentCards, card => Assert.False(card.Value.IsFaceUp));
    }

    [Fact]
    public async Task AiHasAnExplicitVisualIntervalBetweenReveals()
    {
        var ai = new TimedAIService(30);
        await using GameService game = CreateGameService(ai);
        var timestamps = new List<long>();
        game.CardFlipped += (_, _) =>
        {
            if (game.CurrentTurn == GameTurn.AI &&
                game.CurrentCards.Any(card => card.Value.IsFaceUp))
                timestamps.Add(Stopwatch.GetTimestamp());
        };
        await game.StartGameAsync(0, "theme", "0");
        var differentCards = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();

        await game.FlipCardAsync(differentCards[0].Key, differentCards[0].Value);
        await game.FlipCardAsync(differentCards[1].Key, differentCards[1].Value);

        Assert.True(timestamps.Count >= 2);
        double intervalMs = (timestamps[1] - timestamps[0]) * 1000d / Stopwatch.Frequency;
        Assert.True(intervalMs >= 20, $"AI reveal interval was {intervalMs:0.0}ms.");
    }

    [Fact]
    public async Task PlayerCompletingTheLastPairPublishesVictory()
    {
        await using GameService game = CreateGameService(new RecordingAIService([]));
        GameStatisticsEventArgs? result = null;
        game.GameFinished += (_, statistics) => result = statistics;
        await game.StartGameAsync(0, "theme", "0");

        foreach (var pair in game.CurrentCards.GroupBy(card => card.Value.PairId))
        {
            KeyValuePair<int, MemoryCard>[] cards = [.. pair];
            await game.FlipCardAsync(cards[0].Key, cards[0].Value);
            await game.FlipCardAsync(cards[1].Key, cards[1].Value);
        }

        Assert.NotNull(result);
        Assert.True(result!.IsVictory);
    }

    [Fact]
    public async Task AiCompletingTheLastPairPublishesDefeat()
    {
        var ai = new WinningAIService();
        await using GameService game = CreateGameService(ai);
        GameStatisticsEventArgs? result = null;
        game.GameFinished += (_, statistics) => result = statistics;
        await game.StartGameAsync(0, "theme", "0");

        var differentCards = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();
        await game.FlipCardAsync(differentCards[0].Key, differentCards[0].Value);
        await game.FlipCardAsync(differentCards[1].Key, differentCards[1].Value);

        Assert.NotNull(result);
        Assert.False(result!.IsVictory);
    }

    [Fact]
    public async Task PvpGameIsActiveAndAcceptsFirstCard()
    {
        await using GameService game = CreateGameService();

        await game.StartGameAsync(0, "theme", "2");

        Assert.True(game.IsGameActive);
        await game.FlipCardAsync(1, game.CurrentCards[0].Value);
        Assert.True(game.CurrentCards[0].Value.IsFaceUp);
    }

    private static GameService CreateGameService(IAIService? ai = null)
    {
        var settings = new GameSettingsEntity { Options = new() { CardFlipDelayMs = 1 } };
        return new GameService(
            new FakeThemeService(),
            new FakeRepository<GameSettingsEntity>(settings),
            new FakeRepository<GameStatisticsEntity>(),
            new FakeDispatcher(),
            ai ?? new AIService(new FixedRandomSource()));
    }

    private sealed class FakeThemeService : IThemeService
    {
        public Task<IReadOnlyCollection<CardThemeManifestDto>> GetThemesAsync(bool include = false) =>
            Task.FromResult<IReadOnlyCollection<CardThemeManifestDto>>([]);

        public Task<CardThemeDto> GetThemeAsync(string themeName) =>
            Task.FromResult(new CardThemeDto([.. Enumerable.Range(1, 6).Select(i => $"card-{i}")], "theme", null));
    }

    private sealed class FakeRepository<TEntity>(TEntity? entity = default) : IRepository<TEntity>
        where TEntity : EntityBase
    {
        private readonly TEntity? entity = entity;

        public Task AddAsync(TEntity value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TEntity?> GetByIdAsync(string id, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult(entity);
        public Task<TEntity?> GetTrackedByIdAsync(string id, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult(entity);
        public Task<IReadOnlyCollection<TEntity?>> ListTrackedAsync(Expression<Func<TEntity, bool>>? predicate, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<TEntity?>>([entity]);
        public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult(entity);
        public Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TEntity>>([]);
        public Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken) => Task.FromResult(entity is not null);
        public Task UpdateAsync(TEntity value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(TEntity value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveRangeAsync(IEnumerable<TEntity> values, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new FakeTimer();
    }

    private sealed class FakeTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick = default!;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }

    private sealed class FixedRandomSource : IRandomSource
    {
        public int Next(int maxExclusive) => 0;
        public double NextDouble() => 1;
    }

    private sealed class RecordingAIService(List<string> order) : IAIService
    {
        public bool IsPlaying => false;
        public int RememberedCardCount => 0;
        public int VisualRevealDelayMs => 1;
        public void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards) { }
        public void ObserveCard(int position, MemoryCard card) => order.Add("observe");
        public Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default) => Task.FromResult<AITurn?>(null);
        public void CancelPendingTurn() { }
        public void Clear() { }
    }

    private sealed class TimedAIService(int visualRevealDelayMs) : IAIService
    {
        public bool IsPlaying => false;
        public int RememberedCardCount => 0;
        public int VisualRevealDelayMs => visualRevealDelayMs;
        public void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards) { }
        public void ObserveCard(int position, MemoryCard card) { }
        public Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AITurn?>(new AITurn(3, 4));
        public void CancelPendingTurn() { }
        public void Clear() { }
    }

    private sealed class AtomicAiService : IAIService
    {
        public int FirstPosition { get; private set; }
        public int SecondPosition { get; private set; }
        public bool IsPlaying => false;
        public int RememberedCardCount => 0;
        public int VisualRevealDelayMs => 1;

        public void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards)
        {
            KeyValuePair<int, MemoryCard>[] pair = [.. cards.GroupBy(card => card.Value.PairId).ElementAt(2)];
            FirstPosition = pair[0].Key;
            SecondPosition = pair[1].Key;
        }

        public void ObserveCard(int position, MemoryCard card) { }
        public Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AITurn?>(new AITurn(FirstPosition, SecondPosition));
        public void CancelPendingTurn() { }
        public void Clear() { }
    }

    private sealed class WinningAIService : IAIService
    {
        private readonly Queue<AITurn> turns = [];

        public bool IsPlaying => false;
        public int RememberedCardCount => 0;
        public int VisualRevealDelayMs => 1;

        public void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards)
        {
            turns.Clear();
            foreach (var pair in cards.GroupBy(card => card.Value.PairId))
            {
                KeyValuePair<int, MemoryCard>[] positions = [.. pair];
                turns.Enqueue(new AITurn(positions[0].Key, positions[1].Key));
            }
        }

        public void ObserveCard(int position, MemoryCard card) { }
        public Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(turns.Count == 0 ? null : (AITurn?)turns.Dequeue());
        public void CancelPendingTurn() => turns.Clear();
        public void Clear() => turns.Clear();
    }

    [Fact]
    public async Task PlayerMiss_TriggersAiTurn()
    {
        var scriptedAi = new ScriptedAIService();
        await using GameService game = CreateGameService(scriptedAi);
        await game.StartGameAsync(0, "theme", "0");

        var differentCards = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();

        await game.FlipCardAsync(differentCards[0].Key, differentCards[0].Value);
        await game.FlipCardAsync(differentCards[1].Key, differentCards[1].Value);

        Assert.True(scriptedAi.TurnsRequested > 0);
    }

    [Fact]
    public async Task PlayerMatch_AllowsPlayerToPlayAgain()
    {
        var scriptedAi = new ScriptedAIService();
        await using GameService game = CreateGameService(scriptedAi);
        await game.StartGameAsync(0, "theme", "0");

        var pair = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .First()
            .ToArray();

        await game.FlipCardAsync(pair[0].Key, pair[0].Value);
        await game.FlipCardAsync(pair[1].Key, pair[1].Value);

        Assert.Equal(GameTurn.Player, game.CurrentTurn);
        Assert.Equal(0, scriptedAi.TurnsRequested);
        Assert.True(pair[0].Value.IsMatched);
        Assert.True(pair[1].Value.IsMatched);
    }

    [Fact]
    public async Task AiMiss_ReturnsTurnToPlayer()
    {
        var scriptedAi = new ScriptedAIService();
        await using GameService game = CreateGameService(scriptedAi);
        await game.StartGameAsync(0, "theme", "0");

        // Set up scripted AI to flip a mismatching pair
        var differentCards = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Skip(2)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();
        scriptedAi.EnqueueTurn(new AITurn(differentCards[0].Key, differentCards[1].Key));

        // Player misses
        var playerMismatch = game.CurrentCards
            .GroupBy(card => card.Value.PairId)
            .Take(2)
            .SelectMany(group => group.Take(1))
            .ToArray();
        await game.FlipCardAsync(playerMismatch[0].Key, playerMismatch[0].Value);
        await game.FlipCardAsync(playerMismatch[1].Key, playerMismatch[1].Value);

        Assert.Equal(1, scriptedAi.TurnsRequested);
        Assert.Equal(GameTurn.Player, game.CurrentTurn);
        Assert.False(game.IsHumanInteractionBlocked);
    }

    [Fact]
    public async Task AiMatch_AllowsAiToPlayAgain()
    {
        var scriptedAi = new ScriptedAIService();
        await using GameService game = CreateGameService(scriptedAi);
        await game.StartGameAsync(0, "theme", "0");

        // Two matching pairs for AI
        var pair1 = game.CurrentCards.GroupBy(c => c.Value.PairId).ElementAt(2).ToArray();
        var pair2 = game.CurrentCards.GroupBy(c => c.Value.PairId).ElementAt(3).ToArray();
        var mismatch = game.CurrentCards.GroupBy(c => c.Value.PairId).Skip(4).Take(2).SelectMany(g => g.Take(1)).ToArray();

        scriptedAi.EnqueueTurn(new AITurn(pair1[0].Key, pair1[1].Key));
        scriptedAi.EnqueueTurn(new AITurn(pair2[0].Key, pair2[1].Key));
        scriptedAi.EnqueueTurn(new AITurn(mismatch[0].Key, mismatch[1].Key));

        // Player misses to trigger AI
        var playerMismatch = game.CurrentCards.GroupBy(c => c.Value.PairId).Take(2).SelectMany(g => g.Take(1)).ToArray();
        await game.FlipCardAsync(playerMismatch[0].Key, playerMismatch[0].Value);
        await game.FlipCardAsync(playerMismatch[1].Key, playerMismatch[1].Value);

        Assert.Equal(3, scriptedAi.TurnsRequested);
        Assert.True(pair1[0].Value.IsMatched);
        Assert.True(pair2[0].Value.IsMatched);
        Assert.Equal(GameTurn.Player, game.CurrentTurn);
    }

    [Fact]
    public async Task PlayerCompletesLastPair_EndsGameAndNoNewTurnStarted()
    {
        var scriptedAi = new ScriptedAIService();
        await using GameService game = CreateGameService(scriptedAi);
        GameStatisticsEventArgs? result = null;
        game.GameFinished += (_, statistics) => result = statistics;
        await game.StartGameAsync(0, "theme", "0");

        foreach (var pairGroup in game.CurrentCards.GroupBy(card => card.Value.PairId))
        {
            var pair = pairGroup.ToArray();
            await game.FlipCardAsync(pair[0].Key, pair[0].Value);
            await game.FlipCardAsync(pair[1].Key, pair[1].Value);
        }

        Assert.NotNull(result);
        Assert.False(game.IsGameActive);
        Assert.Equal(0, scriptedAi.TurnsRequested);
    }

    [Fact]
    public async Task AiCompletesLastPair_EndsGameAndNoNewTurnStarted()
    {
        var scriptedAi = new ScriptedAIService();
        await using GameService game = CreateGameService(scriptedAi);
        GameStatisticsEventArgs? result = null;
        game.GameFinished += (_, statistics) => result = statistics;
        await game.StartGameAsync(0, "theme", "0");

        // Script AI to match all pairs
        foreach (var pairGroup in game.CurrentCards.GroupBy(card => card.Value.PairId))
        {
            var pair = pairGroup.ToArray();
            scriptedAi.EnqueueTurn(new AITurn(pair[0].Key, pair[1].Key));
        }

        // Player misses once to hand turn to AI
        var playerMismatch = game.CurrentCards.GroupBy(c => c.Value.PairId).Take(2).SelectMany(g => g.Take(1)).ToArray();
        await game.FlipCardAsync(playerMismatch[0].Key, playerMismatch[0].Value);
        await game.FlipCardAsync(playerMismatch[1].Key, playerMismatch[1].Value);

        Assert.NotNull(result);
        Assert.False(game.IsGameActive);
        Assert.True(game.CurrentCards.All(c => c.Value.IsMatched));
    }

    [Fact]
    public async Task NoTurnInitiatedAfterGameFinished()
    {
        var scriptedAi = new ScriptedAIService();
        await using GameService game = CreateGameService(scriptedAi);
        int gameFinishedCount = 0;
        game.GameFinished += (_, _) => gameFinishedCount++;
        await game.StartGameAsync(0, "theme", "0");

        foreach (var pairGroup in game.CurrentCards.GroupBy(card => card.Value.PairId))
        {
            var pair = pairGroup.ToArray();
            await game.FlipCardAsync(pair[0].Key, pair[0].Value);
            await game.FlipCardAsync(pair[1].Key, pair[1].Value);
        }

        Assert.Equal(1, gameFinishedCount);
        Assert.False(game.IsGameActive);

        // Attempt further interaction after finish
        var firstCard = game.CurrentCards.First();
        await game.FlipCardAsync(firstCard.Key, firstCard.Value);

        Assert.Equal(1, gameFinishedCount);
        Assert.Equal(0, scriptedAi.TurnsRequested);
    }

    [Fact]
    public async Task RestartGame_CancelsPreviousGenerationAndPreventsStaleMoveLeak()
    {
        var delayAi = new DelayedAiService(50);
        await using GameService game = CreateGameService(delayAi);
        await game.StartGameAsync(0, "theme", "0");

        // Player misses to trigger AI
        var mismatch = game.CurrentCards.GroupBy(c => c.Value.PairId).Take(2).SelectMany(g => g.Take(1)).ToArray();
        _ = game.FlipCardAsync(mismatch[0].Key, mismatch[0].Value);
        _ = game.FlipCardAsync(mismatch[1].Key, mismatch[1].Value);

        // Immediately restart game
        await game.StartGameAsync(0, "theme", "0");

        Assert.True(game.IsGameActive);
        Assert.Equal(GameTurn.Player, game.CurrentTurn);
        Assert.All(game.CurrentCards, c => Assert.False(c.Value.IsMatched));
    }

    private sealed class ScriptedAIService : IAIService
    {
        private readonly Queue<AITurn> _turns = [];
        public int TurnsRequested { get; private set; }
        public bool IsPlaying => false;
        public int RememberedCardCount => 0;
        public int VisualRevealDelayMs => 1;

        public void EnqueueTurn(AITurn turn) => _turns.Enqueue(turn);

        public void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards) { }
        public void ObserveCard(int position, MemoryCard card) { }
        public Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default)
        {
            TurnsRequested++;
            return Task.FromResult(_turns.Count > 0 ? (AITurn?)_turns.Dequeue() : null);
        }
        public void CancelPendingTurn() => _turns.Clear();
        public void Clear() => _turns.Clear();
    }

    private sealed class DelayedAiService(int delayMs) : IAIService
    {
        public bool IsPlaying => false;
        public int RememberedCardCount => 0;
        public int VisualRevealDelayMs => delayMs;
        public void StartGame(GameDifficulty difficulty, IReadOnlyCollection<KeyValuePair<int, MemoryCard>> cards) { }
        public void ObserveCard(int position, MemoryCard card) { }
        public async Task<AITurn?> GetNextTurnAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(delayMs, cancellationToken);
            return new AITurn(1, 2);
        }
        public void CancelPendingTurn() { }
        public void Clear() { }
    }
}
