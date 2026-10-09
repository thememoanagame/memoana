using System.ComponentModel;
using System.Runtime.CompilerServices;
using MemoAna.Infrastructure.Api;
using MemoAna.Infrastructure.Assets;
using MemoAna.Infrastructure.SignalR;

namespace MemoAna.Infrastructure.Session;

public sealed class GameSessionCoordinator(IGameApiClient api, IGameSessionStore sessions, IGameAssetStore assets, IGameHubClient hub, ILogger<GameSessionCoordinator> logger) : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly Dictionary<string, string> _assetUris = new(StringComparer.Ordinal);
    private GameSession? _session;
    private GameState? _state;
    private string? _error;
    private bool _initialized;

    public event PropertyChangedEventHandler? PropertyChanged;
    public GameSession? Session => _session;
    public GameState? State => _state;
    public IReadOnlyDictionary<string, string> AssetUris => _assetUris;
    public GameConnectionState ConnectionState => hub.ConnectionState;
    public string? Error => _error;
    public bool IsBusy { get; private set; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        _initialized = true;
        _session = await sessions.LoadAsync(ct);
        if (_session is null) return;
        try { await hub.ReconnectAsync(new(_session.RoomId, _session.PlayerId, _session.AccessToken), ct); await RefreshAndPrepareAsync(ct); }
        catch (Exception ex) { SetError(ToUserMessage(ex)); }
    }

    public async Task CreateAndJoinAsync(GameMode mode, GameDifficulty difficulty, string? themeId = null, CancellationToken ct = default)
    {
        await RunBusyAsync(async () =>
        {
            var room = await api.CreateRoomAsync(new CreateRoomRequest(mode, difficulty, themeId), ct);
            await JoinAsync(room.RoomId, ct);
        });
    }

    public Task JoinAsync(string roomId, CancellationToken ct = default) => RunBusyAsync(async () =>
    {
        var joined = await hub.JoinAsync(roomId.Trim(), ct);
        _session = new GameSession(joined.RoomId, joined.PlayerId, joined.AccessToken, joined.Mode, joined.Difficulty, joined.ThemeId);
        await sessions.SaveAsync(_session, ct);
        await RefreshAndPrepareAsync(ct);
    });

    public Task FlipAsync(int position, CancellationToken ct = default) => RunBusyAsync(async () =>
    {
        if (_session is null) throw new InvalidOperationException("Entre em uma sala antes de jogar.");
        var result = await hub.FlipCardAsync(_session.RoomId, position, ct);
        if (!result.Succeeded) throw new GameApiException(result.ErrorCode ?? "move_failed", result.ErrorMessage ?? "A jogada não foi aceita.");
        await RefreshAsync(ct);
    });

    public async Task LeaveAsync(CancellationToken ct = default)
    {
        if (_session is not null) { try { await hub.LeaveAsync(_session.RoomId, ct); } catch { } }
        await sessions.ClearAsync(ct); _session = null; _state = null; _assetUris.Clear(); Notify(nameof(Session)); Notify(nameof(State));
    }

    private async Task RefreshAndPrepareAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        if (_session is null || _state?.Status is GameStatus.Finished) return;
        try
        {
            var manifest = await api.GetManifestAsync(_session.RoomId, _session.AccessToken, ct);
            foreach (var asset in manifest.Assets)
                _assetUris[asset.AssetToken] = await assets.EnsureAssetAsync(_session.RoomId, asset.AssetToken, asset.ContentType, () => api.DownloadAssetAsync(_session.RoomId, _session.AccessToken, asset.AssetToken, ct), ct);
            var ready = await hub.AssetsReadyAsync(_session.RoomId, ct);
            if (!ready.Succeeded && ready.ErrorCode != "assets_already_ready") throw new GameApiException(ready.ErrorCode ?? "assets_ready_failed", ready.ErrorMessage ?? "Não foi possível preparar os assets.");
            await RefreshAsync(ct);
            Notify(nameof(AssetUris));
        }
        catch (GameApiException ex) when (ex.Code == "assets_not_available") { }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (_session is null) return;
        var state = await api.GetStateAsync(_session.RoomId, _session.AccessToken, ct);
        _state = state; Notify(nameof(State));
    }

    private async void OnServerEvent(object? sender, EventArgs e)
    {
        try { await _stateGate.WaitAsync(); await RefreshAndPrepareAsync(); }
        catch (Exception ex) { logger.LogDebug(ex, "Could not synchronize game event"); }
        finally { if (_stateGate.CurrentCount == 0) _stateGate.Release(); }
    }

    private void OnConnectionStateChanged(object? sender, GameConnectionState e) { Notify(nameof(ConnectionState)); }
    private async void OnTransportReconnected(object? sender, EventArgs e)
    {
        if (_session is null) return;
        try { await hub.ReconnectAsync(new(_session.RoomId, _session.PlayerId, _session.AccessToken)); await RefreshAndPrepareAsync(); }
        catch (Exception ex) { SetError(ToUserMessage(ex)); }
    }
    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true; SetError(null); Notify(nameof(IsBusy));
        try { await action(); } catch (Exception ex) { SetError(ToUserMessage(ex)); }
        finally { IsBusy = false; Notify(nameof(IsBusy)); }
    }
    private void SetError(string? error) { _error = error; Notify(nameof(Error)); }
    private static string ToUserMessage(Exception ex) => ex switch
    {
        GameApiException e when e.Code is "room_not_found" or "not_found" => "A sala não existe ou não está mais disponível.",
        GameApiException e when e.Code is "room_full" => "A sala já está cheia.",
        GameApiException e when e.Code is "invalid_participation_token" => "A sessão salva não é mais válida.",
        GameApiException e => e.Message,
        HttpRequestException => "Não foi possível conectar à API. Verifique a rede e o endereço do servidor.",
        _ => "Não foi possível concluir a operação. Tente novamente."
    };
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public async ValueTask DisposeAsync() { hub.ServerEventReceived -= OnServerEvent; hub.ConnectionStateChanged -= OnConnectionStateChanged; hub.TransportReconnected -= OnTransportReconnected; await hub.DisposeAsync(); _stateGate.Dispose(); }
    public void Start() { hub.ServerEventReceived += OnServerEvent; hub.ConnectionStateChanged += OnConnectionStateChanged; hub.TransportReconnected += OnTransportReconnected; }
}
