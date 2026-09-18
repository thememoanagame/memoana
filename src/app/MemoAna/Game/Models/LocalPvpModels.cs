using System.Text.Json;
using MemoAna.Game.Core;
using MemoAna.Game.Enums;

namespace MemoAna.Game.Models;

public enum LocalPvpMessageType
{
    Hello,
    HelloAccepted,
    HelloRejected,
    GameConfiguration,
    GameReady,
    CardFlip,
    TurnResult,
    GameFinished,
    Disconnect,
    Error
}

public sealed record LocalPvpRoom(
    string RoomId,
    string HostName,
    string HostAddress,
    int TcpPort,
    DateTime LastSeenUtc);

public sealed record LocalPvpBoardCard(
    int Position,
    int Id,
    string PairId,
    string? CardImage);

public sealed record LocalPvpGameConfiguration(
    string RoomId,
    GameDifficulty Difficulty,
    string ThemeName,
    IReadOnlyList<LocalPvpBoardCard> Board,
    string InitialPlayerId);

public sealed record LocalPvpCardFlip(int Position, int TurnId, string PlayerId);

public sealed record LocalPvpStatistics(
    int HostScore,
    int ClientScore,
    int HostSuccessfulMoves,
    int ClientSuccessfulMoves,
    int HostMistakes,
    int ClientMistakes,
    int HostFinalScore,
    int ClientFinalScore,
    string? WinnerPlayerId);

public sealed record LocalPvpTurnResult(
    int TurnId,
    int FirstPosition,
    int SecondPosition,
    bool IsMatch,
    string PlayerId,
    string NextPlayerId,
    LocalPvpStatistics Statistics,
    bool IsGameFinished = false);

public sealed class LocalPvpMessageEventArgs(
    LocalPvpMessageType messageType,
    JsonElement payload) : System.EventArgs
{
    public LocalPvpMessageType MessageType { get; } = messageType;
    public JsonElement Payload { get; } = payload;

    public T Deserialize<T>(JsonSerializerOptions options) =>
        Payload.Deserialize<T>(options)
        ?? throw new InvalidOperationException($"Mensagem PVP inválida: {MessageType}.");
}

public sealed class LocalPvpRoomsChangedEventArgs(IReadOnlyList<LocalPvpRoom> rooms) : System.EventArgs
{
    public IReadOnlyList<LocalPvpRoom> Rooms { get; } = rooms;
}

public sealed class LocalPvpConnectionEventArgs(bool connected, string? playerName, string? error = null) : System.EventArgs
{
    public bool Connected { get; } = connected;
    public string? PlayerName { get; } = playerName;
    public string? Error { get; } = error;
}

public static class LocalPvpCardExtensions
{
    public static MemoryCard ToMemoryCard(this LocalPvpBoardCard card) => new()
    {
        Id = card.Id,
        PairId = card.PairId,
        CardImage = card.CardImage
    };
}

public static class LocalPvpJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
