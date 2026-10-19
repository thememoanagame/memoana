using MemoAna.Game.Core;
using MemoAna.Game.Models;

namespace MemoAna.Game.Abstract.Services;

public interface IPVPService : IAsyncDisposable
{
    IReadOnlyList<KeyValuePair<int, MemoryCard>> CurrentCards { get; }
    Guid? SessionId { get; }
    PVPPlayerRole LocalRole { get; }
    PVPPlayerRole CurrentTurn { get; }
    bool IsHost { get; }
    bool IsConnected { get; }
    bool IsGameActive { get; }
    bool IsLocalTurn { get; }
    string LocalPlayerName { get; }
    string RemotePlayerName { get; }
    int LocalScore { get; }
    int RemoteScore { get; }
    PVPPlayerStatistics LocalStatistics { get; }
    PVPPlayerStatistics RemoteStatistics { get; }

    event EventHandler? HostStarted;
    event EventHandler? Connected;
    event EventHandler<PVPHostDiscoveredEventArgs>? HostDiscovered;
    event EventHandler<PVPStateChangedEventArgs>? SessionStarted;
    event EventHandler<PVPCardEventArgs>? CardChanged;
    event EventHandler<PVPStateChangedEventArgs>? TurnChanged;
    event EventHandler<PVPGameFinishedEventArgs>? GameFinished;
    event EventHandler? PlayerDisconnected;
    event EventHandler<PVPErrorEventArgs>? Error;

    Task StartHostAsync(PVPGameSettings settings, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PVPHostInfo>> DiscoverHostsAsync(
        int discoveryPort = 35679,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
    Task ConnectAsync(PVPHostInfo host, string playerName, CancellationToken cancellationToken = default);
    Task StartGameAsync(CancellationToken cancellationToken = default);
    Task FlipCardAsync(int position, CancellationToken cancellationToken = default);
    Task LeaveGameAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
