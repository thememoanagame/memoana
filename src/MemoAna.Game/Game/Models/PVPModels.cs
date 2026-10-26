using MemoAna.Game.Enums;

namespace MemoAna.Game.Models;

public enum PVPPlayerRole
{
    Host,
    Client
}

public enum PVPMessageType
{
    ConnectionHello,
    ConnectionAccepted,
    DiscoveryRequest,
    DiscoveryResponse,
    GameStart,
    CardFlipRequest,
    CardFlipped,
    TurnResolved,
    GameFinished,
    PlayerDisconnected
}

public sealed record PVPGameSettings(
    int Difficulty,
    string ThemeName,
    string PlayerName,
    int Port = 35678,
    int DiscoveryPort = 35679);

public sealed record PVPHostInfo(
    string HostName,
    string Address,
    int Port,
    int DiscoveryPort,
    int Difficulty,
    string ThemeName,
    int ConnectedPlayers,
    int MaxPlayers,
    string ProtocolVersion);

public sealed record PVPCardState(
    int Position,
    string PairId,
    int ImageIndex,
    bool IsFaceUp = false,
    bool IsMatched = false);

public sealed record PVPGameState(
    Guid SessionId,
    string ThemeName,
    int Difficulty,
    IReadOnlyList<PVPCardState> Cards,
    PVPPlayerRole CurrentTurn,
    int HostScore,
    int ClientScore);

public sealed record PVPGameResult(
    Guid SessionId,
    PVPPlayerRole Winner,
    int HostScore,
    int ClientScore,
    string ThemeName = "",
    int Difficulty = 0,
    DateTime PlayedAt = default,
    IReadOnlyList<PVPPlayerStatistics>? Statistics = null);

public sealed record PVPPlayerStatistics(
    PVPPlayerRole Role,
    string Name,
    int Score,
    int Hits,
    int Misses,
    int TotalMoves)
{
    public int FinalScore => Score;
}

public sealed class PVPStateChangedEventArgs(PVPGameState state) : System.EventArgs
{
    public PVPGameState State { get; } = state;
}

public sealed class PVPHostDiscoveredEventArgs(PVPHostInfo host) : System.EventArgs
{
    public PVPHostInfo Host { get; } = host;
}

public sealed class PVPCardEventArgs(int position, PVPPlayerRole player, bool matched) : System.EventArgs
{
    public int Position { get; } = position;
    public PVPPlayerRole Player { get; } = player;
    public bool Matched { get; } = matched;
}

public sealed class PVPErrorEventArgs(string message, Exception? exception = null) : System.EventArgs
{
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}

public sealed class PVPGameFinishedEventArgs(PVPGameResult result) : System.EventArgs
{
    public PVPGameResult Result { get; } = result;
}
