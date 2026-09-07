using System.Linq.Expressions;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Entities;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;
using MemoAna.Game.Enums;
using MemoAna.Game.Services;
using Microsoft.Maui.Dispatching;
using Xunit;

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

    private static GameService CreateGameService()
    {
        var settings = new GameSettingsEntity { Options = new() { CardFlipDelayMs = 1 } };
        return new GameService(
            new FakeThemeService(),
            new FakeRepository<GameSettingsEntity>(settings),
            new FakeRepository<GameStatisticsEntity>(),
            new FakeDispatcher(),
            new AIService(new FixedRandomSource()));
    }

    private sealed class FakeThemeService : IThemeService
    {
        public Task<IReadOnlyCollection<CardThemeManifestDto>> GetThemesAsync() =>
            Task.FromResult<IReadOnlyCollection<CardThemeManifestDto>>([]);

        public Task<CardThemeDto> GetThemeAsync(string themeName) =>
            Task.FromResult(new CardThemeDto(Enumerable.Range(1, 6).Select(i => $"card-{i}").ToList(), "theme", null));
    }

    private sealed class FakeRepository<TEntity>(TEntity? entity = null) : IRepository<TEntity>
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
        public event EventHandler? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }

    private sealed class FixedRandomSource : IRandomSource
    {
        public int Next(int maxExclusive) => 0;
        public double NextDouble() => 1;
    }
}
