using MemoAna.Game.Models;

namespace MemoAna.Game.Abstract.Services;

public interface ILocalPVPService : IAsyncDisposable
{
    bool IsHost { get; }
    bool IsConfigured { get; }
    bool IsConnected { get; }
    string? RoomId { get; }
    string? LocalPlayerName { get; }
    string? RemotePlayerName { get; }
    IReadOnlyList<LocalPvpRoom> DiscoveredRooms { get; }

    event EventHandler<LocalPvpRoomsChangedEventArgs>? RoomsChanged;
    event EventHandler<LocalPvpConnectionEventArgs>? ConnectionChanged;
    event EventHandler<LocalPvpMessageEventArgs>? MessageReceived;

    Task StartHostAsync(string playerName, CancellationToken cancellationToken = default);
    Task StartDiscoveryAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(LocalPvpRoom room, string playerName, CancellationToken cancellationToken = default);
    Task WaitForPeerAsync(CancellationToken cancellationToken = default);
    Task<LocalPvpMessageEventArgs> WaitForMessageAsync(LocalPvpMessageType messageType, CancellationToken cancellationToken = default);
    Task SendGameConfigurationAsync(LocalPvpGameConfiguration configuration, CancellationToken cancellationToken = default);
    Task SendReadyAsync(CancellationToken cancellationToken = default);
    Task SendCardFlipAsync(LocalPvpCardFlip flip, CancellationToken cancellationToken = default);
    Task SendTurnResultAsync(LocalPvpTurnResult result, CancellationToken cancellationToken = default);
    Task SendGameFinishedAsync(LocalPvpTurnResult result, CancellationToken cancellationToken = default);
    Task LeaveAsync();
}
