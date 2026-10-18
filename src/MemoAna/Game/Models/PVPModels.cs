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
    int ClientScore);

public sealed class PVPStateChangedEventArgs : System.EventArgs
{
    public PVPStateChangedEventArgs(PVPGameState state) => State = state;
    public PVPGameState State { get; }
}

public sealed class PVPHostDiscoveredEventArgs : System.EventArgs
{
    public PVPHostDiscoveredEventArgs(PVPHostInfo host) => Host = host;
    public PVPHostInfo Host { get; }
}

public sealed class PVPCardEventArgs : System.EventArgs
{
    public PVPCardEventArgs(int position, PVPPlayerRole player, bool matched)
    {
        Position = position;
        Player = player;
        Matched = matched;
    }

    public int Position { get; }
    public PVPPlayerRole Player { get; }
    public bool Matched { get; }
}

public sealed class PVPErrorEventArgs : System.EventArgs
{
    public PVPErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }

    public string Message { get; }
    public Exception? Exception { get; }
}

public sealed class PVPGameFinishedEventArgs : System.EventArgs
{
    public PVPGameFinishedEventArgs(PVPGameResult result) => Result = result;
    public PVPGameResult Result { get; }
}
