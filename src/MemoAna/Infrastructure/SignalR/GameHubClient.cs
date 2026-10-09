using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MemoAna.Infrastructure.Api;
using MemoAna.Infrastructure.Session;

namespace MemoAna.Infrastructure.SignalR;

public sealed class GameHubClient(Uri apiBaseUri, JsonSerializerOptions jsonOptions, ILogger<GameHubClient> logger) : IGameHubClient
{
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions;
    private readonly ILogger<GameHubClient> _logger = logger;
    private readonly HubConnection _connection = new HubConnectionBuilder()
        .WithUrl(new Uri(apiBaseUri, "gameHub"))
        .WithAutomaticReconnect()
        .Build();
    private GameConnectionState _state = GameConnectionState.Disconnected;
    private bool _handlersRegistered;

    public event EventHandler<GameConnectionState>? ConnectionStateChanged;
    public event EventHandler? TransportReconnected;
    public event EventHandler? ServerEventReceived;
    public GameConnectionState ConnectionState => _state;

    public async Task<JoinRoomResponse> JoinAsync(string roomId, CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);
        var result = await _connection.InvokeAsync<GameOperationResult>("JoinRoom", roomId, ct);
        return ReadJoinResult(result);
    }

    public async Task<JoinRoomResponse> ReconnectAsync(GameSessionInfo session, CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);
        var result = await _connection.InvokeAsync<GameOperationResult>("ReconnectRoom", session.RoomId, session.PlayerId, session.AccessToken, ct);
        return ReadJoinResult(result);
    }

    public Task<GameState?> GetStateAsync(string roomId, CancellationToken ct = default) => _connection.InvokeAsync<GameState?>("GetState", roomId, ct);
    public Task<GameOperationResult> AssetsReadyAsync(string roomId, CancellationToken ct = default) => _connection.InvokeAsync<GameOperationResult>("AssetsReady", roomId, ct);
    public Task<GameOperationResult> FlipCardAsync(string roomId, int position, CancellationToken ct = default) => _connection.InvokeAsync<GameOperationResult>("FlipCard", roomId, position, ct);
    public async Task LeaveAsync(string roomId, CancellationToken ct = default) { if (_connection.State == HubConnectionState.Connected) await _connection.InvokeAsync<GameOperationResult>("LeaveRoom", roomId, ct); }

    private async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (_connection.State == HubConnectionState.Connected) return;
        SetState(GameConnectionState.Connecting);
        RegisterHandlers();
        try { await _connection.StartAsync(ct); SetState(GameConnectionState.Connected); }
        catch { SetState(GameConnectionState.Disconnected); throw; }
    }

    private void RegisterHandlers()
    {
        if (_handlersRegistered) return;
        _handlersRegistered = true;
        _connection.Reconnecting += _ => { SetState(GameConnectionState.Reconnecting); return Task.CompletedTask; };
        _connection.Reconnected += _ => { SetState(GameConnectionState.Connected); TransportReconnected?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; };
        _connection.Closed += _ => { SetState(GameConnectionState.Disconnected); return Task.CompletedTask; };
        foreach (var name in new[] { "PlayerJoined", "PlayerLeft", "GamePreparing", "AssetsAvailable", "AssetsReady", "GameStarted", "CardRevealed", "PairMatched", "PairMissed", "TurnChanged", "ScoreUpdated", "GameFinished", "Error" })
            _connection.On<JsonElement>(name, _ => ServerEventReceived?.Invoke(this, EventArgs.Empty));
    }

    private void SetState(GameConnectionState state) { _state = state; ConnectionStateChanged?.Invoke(this, state); }

    private JoinRoomResponse ReadJoinResult(GameOperationResult result)
    {
        if (!result.Succeeded) throw new GameApiException(result.ErrorCode ?? "join_failed", result.ErrorMessage ?? "Não foi possível entrar na sala.");
        if (result.Value is JoinRoomResponse response) return response;
        if (result.Value is JsonElement json && json.Deserialize<JoinRoomResponse>(_jsonOptions) is { } parsed) return parsed;
        throw new GameApiException("invalid_join_response", "A API retornou uma identidade de participação inválida.");
    }

    public async ValueTask DisposeAsync() { try { await _connection.DisposeAsync(); } catch (Exception ex) { _logger.LogDebug(ex, "SignalR connection disposal failed"); } }
}
