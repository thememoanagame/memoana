using MemoAna.Infrastructure.Api;

namespace MemoAna.Infrastructure.SignalR;

public enum GameConnectionState { Disconnected, Connecting, Connected, Reconnecting }

public interface IGameHubClient : IAsyncDisposable
{
    event EventHandler<GameConnectionState>? ConnectionStateChanged;
    event EventHandler? TransportReconnected;
    event EventHandler? ServerEventReceived;
    GameConnectionState ConnectionState { get; }
    Task<JoinRoomResponse> JoinAsync(string roomId, CancellationToken cancellationToken = default);
    Task<JoinRoomResponse> ReconnectAsync(GameSessionInfo session, CancellationToken cancellationToken = default);
    Task<GameState?> GetStateAsync(string roomId, CancellationToken cancellationToken = default);
    Task<GameOperationResult> AssetsReadyAsync(string roomId, CancellationToken cancellationToken = default);
    Task<GameOperationResult> FlipCardAsync(string roomId, int position, CancellationToken cancellationToken = default);
    Task LeaveAsync(string roomId, CancellationToken cancellationToken = default);
}

public sealed record GameSessionInfo(string RoomId, string PlayerId, string AccessToken);
