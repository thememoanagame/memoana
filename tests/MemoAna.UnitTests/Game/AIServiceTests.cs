using MemoAna.Game.Core;
using MemoAna.Game.Enums;
using MemoAna.Game.Models;
using MemoAna.Game.Services;
using Xunit;

namespace MemoAna.UnitTests.Game;

public sealed class AIServiceTests
{
    [Fact]
    public async Task Easy_UsesLimitedMemory()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(8);
        ai.StartGame(GameDifficulty.Easy, cards);
        foreach (var card in cards.Take(4))
            Observe(ai, card);

        await ai.GetNextTurnAsync();

        Assert.InRange(ai.RememberedCardCount, 0, 3);
    }

    [Fact]
    public async Task Medium_UsesIntermediateMemory()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(12);
        ai.StartGame(GameDifficulty.Medium, cards);
        foreach (var card in cards)
            Observe(ai, card);

        await ai.GetNextTurnAsync();

        Assert.Equal(6, ai.RememberedCardCount);
    }

    [Fact]
    public async Task Hard_PrioritizesKnownPair()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(6);
        ai.StartGame(GameDifficulty.Hard, cards);
        Observe(ai, cards[0]);
        Observe(ai, cards[1]);

        AITurn? turn = await ai.GetNextTurnAsync();

        Assert.Equal(new AITurn(1, 2), turn);
    }

    [Fact]
    public async Task Hard_RetainsMemoryUntilTheEnd()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(8);
        ai.StartGame(GameDifficulty.Hard, cards);
        foreach (var card in cards)
            Observe(ai, card);

        await ai.GetNextTurnAsync();

        Assert.Equal(6, ai.RememberedCardCount);
    }

    [Fact]
    public async Task StartingANewGame_ClearsMemory()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(4);
        ai.StartGame(GameDifficulty.Hard, cards);
        Observe(ai, cards[0]);
        Observe(ai, cards[1]);
        ai.StartGame(GameDifficulty.Hard, cards);

        await ai.GetNextTurnAsync();

        Assert.Equal(0, ai.RememberedCardCount);
    }

    [Fact]
    public async Task CancelPendingTurn_StopsThinking()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(4);
        ai.StartGame(GameDifficulty.Hard, cards);
        Task<AITurn?> pending = ai.GetNextTurnAsync();
        ai.CancelPendingTurn();

        Assert.Null(await pending);
        Assert.False(ai.IsPlaying);
    }

    [Fact]
    public async Task MemoryStartsEmpty_AndUnknownPairIsNotSelectedAsKnown()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(6);

        ai.StartGame(GameDifficulty.Hard, cards);

        Assert.Equal(0, ai.RememberedCardCount);
        AITurn? turn = await ai.GetNextTurnAsync();

        Assert.NotEqual(new AITurn(3, 4), turn);
    }

    [Fact]
    public void ObserveCard_AddsMemory_AndMatchedCardIsRemoved()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(4);
        ai.StartGame(GameDifficulty.Hard, cards);

        Observe(ai, cards[0]);
        Assert.Equal(1, ai.RememberedCardCount);

        cards[0].Value.IsMatched = true;
        ai.ObserveCard(1, cards[0].Value);

        Assert.Equal(0, ai.RememberedCardCount);
    }

    [Fact]
    public void HiddenCardIsNotObservedAndPreviousGenerationCannotContaminateMemory()
    {
        var ai = new AIService(new FixedRandomSource());
        var previousCards = Cards(4);
        ai.StartGame(GameDifficulty.Hard, previousCards);
        previousCards[0].Value.IsFaceUp = true;
        ai.ObserveCard(1, previousCards[0].Value);

        var currentCards = Cards(4);
        ai.StartGame(GameDifficulty.Hard, currentCards);
        ai.ObserveCard(1, previousCards[0].Value);
        ai.ObserveCard(2, currentCards[1].Value);

        Assert.Equal(0, ai.RememberedCardCount);
    }

    [Fact]
    public async Task KnownPairRequiresBothCardsToHaveBeenObserved()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(6);
        ai.StartGame(GameDifficulty.Hard, cards);

        Observe(ai, cards[2]);
        AITurn? turn = await ai.GetNextTurnAsync();

        Assert.NotEqual(new AITurn(3, 4), turn);
    }

    [Fact]
    public async Task MemoryExpiresAfterRetentionTurns()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(6);
        ai.StartGame(GameDifficulty.Easy, cards);
        Observe(ai, cards[0]);

        for (int i = 0; i < 4; i++)
            await ai.GetNextTurnAsync();

        Assert.Equal(0, ai.RememberedCardCount);
    }

    [Fact]
    public async Task FaceUpAndMatchedCardsAreNeverSelected()
    {
        var ai = new AIService(new FixedRandomSource());
        var cards = Cards(6);
        cards[0].Value.IsFaceUp = true;
        cards[1].Value.IsMatched = true;
        ai.StartGame(GameDifficulty.Hard, cards);

        AITurn? turn = await ai.GetNextTurnAsync();

        Assert.NotNull(turn);
        Assert.DoesNotContain(1, new[] { turn!.FirstPosition, turn.SecondPosition });
        Assert.DoesNotContain(2, new[] { turn.FirstPosition, turn.SecondPosition });
    }

    [Fact]
    public async Task DeliberateErrorChanceCanAvoidKnownPairDeterministically()
    {
        var ai = new AIService(new DeliberateErrorRandomSource());
        var cards = Cards(6);
        ai.StartGame(GameDifficulty.Easy, cards);
        Observe(ai, cards[0]);
        Observe(ai, cards[1]);

        AITurn? turn = await ai.GetNextTurnAsync();

        Assert.NotEqual(new AITurn(1, 2), turn);
    }

    private static List<KeyValuePair<int, MemoryCard>> Cards(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new KeyValuePair<int, MemoryCard>(i,
                new MemoryCard { Id = i, PairId = $"pair-{(i - 1) / 2}" }))
            .ToList();

    private static void Observe(AIService ai, KeyValuePair<int, MemoryCard> card)
    {
        card.Value.IsFaceUp = true;
        ai.ObserveCard(card.Key, card.Value);
        card.Value.IsFaceUp = false;
    }

    private sealed class FixedRandomSource : IRandomSource
    {
        public int Next(int maxExclusive) => 0;
        public double NextDouble() => 1;
    }

    private sealed class DeliberateErrorRandomSource : IRandomSource
    {
        public int Next(int maxExclusive) => 0;
        public double NextDouble() => 0;
    }
}
