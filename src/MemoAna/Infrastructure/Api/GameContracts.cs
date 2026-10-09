using System.Text.Json.Serialization;

namespace MemoAna.Infrastructure.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameMode { Time, PVP, AI }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameDifficulty { Easy, Medium, Hard }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameStatus { Waiting, Preparing, Playing, Finished }

public sealed record ThemeSummary(string Id, string Name, bool Available, int CardCount, IReadOnlyList<GameDifficulty> SupportedDifficulties, string? PreviewReference);
public sealed record DifficultyOption(GameDifficulty Difficulty, int PairCount, int CardCount);
public sealed record CreateRoomRequest(GameMode Mode, GameDifficulty Difficulty, string? ThemeId = null);
public sealed record CreateRoomResponse(string RoomId, GameMode Mode, GameDifficulty Difficulty, GameStatus Status, string ThemeId, int PairCount, int CardCount);
public sealed record JoinRoomResponse(string RoomId, string PlayerId, string AccessToken, GameMode Mode, GameDifficulty Difficulty, GameStatus Status, string ThemeId, int PairCount, IReadOnlyList<CardView> Board, string? CurrentTurn);
public sealed record AssetManifest(string RoomId, string ThemeId, int PairCount, IReadOnlyList<AssetManifestEntry> Assets);
public sealed record AssetManifestEntry(string AssetToken, string ContentType, long Size, string Reference);
public sealed record CardView(int Position, bool IsRevealed, bool IsMatched, string? AssetReference);
public sealed record GameState(string RoomId, GameMode Mode, GameDifficulty Difficulty, GameStatus Status, string ThemeId, IReadOnlyList<string> Players, IReadOnlyList<CardView> Board, string? CurrentTurn, IReadOnlyDictionary<string, int> Scores, IReadOnlyDictionary<string, int> ConsecutiveHits, DateTimeOffset? StartedAt, TimeSpan? Duration);
public sealed record GameError(string Code, string Message);
public sealed class GameOperationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public object? Value { get; init; }
}
