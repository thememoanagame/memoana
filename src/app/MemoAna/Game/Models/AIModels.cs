using MemoAna.Game.Core;
using MemoAna.Game.Enums;

namespace MemoAna.Game.Models;

public sealed record AITurn(int FirstPosition, int SecondPosition);

public sealed record AIDifficultyOptions(
    int MemoryCapacity,
    int MemoryRetentionTurns,
    double KnownPairPriority,
    double DeliberateErrorChance,
    int ThinkingDelayMs);

public static class AIDifficultyOptionsFactory
{
    public static AIDifficultyOptions For(GameDifficulty difficulty, int cardCount) => difficulty switch
    {
        GameDifficulty.Easy => new(3, 3, 0.20, 0.40, 650),
        GameDifficulty.Medium => new(6, 10, 0.75, 0.15, 450),
        GameDifficulty.Hard => new(Math.Max(1, (int)Math.Ceiling(cardCount * 0.70)),
            int.MaxValue, 1.0, 0.0, 250),
        _ => new(3, 3, 0.20, 0.40, 650)
    };
}

public interface IRandomSource
{
    int Next(int maxExclusive);
    double NextDouble();
}

public sealed class RandomSource : IRandomSource
{
    private readonly Random random = new();
    public int Next(int maxExclusive) => random.Next(maxExclusive);
    public double NextDouble() => random.NextDouble();
}
