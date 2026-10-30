using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Abstract;
using MemoAna.Server.Application.Relay.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MemoAna.Server.Infrastructure.Services.Concrete;

internal sealed class RelayPeerSession(string roomId, bool isHost)
{
    public string RoomId { get; } = roomId;
    public bool IsHost { get; } = isHost;
}

public sealed class RelayRoom
{
    public string RoomId { get; init; } = default!;
    public string RoomName { get; init; } = default!;
    public string HostPlayerId { get; init; } = default!;
    public string HostPlayerName { get; init; } = default!;
    public string? GuestPlayerId { get; set; }
    public string? GuestPlayerName { get; set; }
    public string? PasswordHash { get; init; }
    public bool RequiresPassword => !string.IsNullOrEmpty(PasswordHash);
    public string Theme { get; init; } = default!;
    public int Difficulty { get; init; }
    public RelayRoomStatus Status { get; set; } = RelayRoomStatus.WaitingForGuest;
    public string HostToken { get; init; } = default!;
    public string GuestToken { get; init; } = default!;
    public NetPeer? HostConnection { get; set; }
    public NetPeer? GuestConnection { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public object SyncRoot { get; } = new();

    public bool IsExpired(TimeSpan inactivityTimeout, TimeSpan unconnectedTimeout)
    {
        DateTime now = DateTime.UtcNow;
        if (HostConnection == null && GuestConnection == null)
        {
            return (now - CreatedAt) > unconnectedTimeout || (now - LastActivity) > inactivityTimeout;
        }

        return (now - LastActivity) > inactivityTimeout;
    }

    public bool IsClosed => Status == RelayRoomStatus.Closed;

    public bool VerifyPassword(string? password)
    {
        if (string.IsNullOrEmpty(PasswordHash))
            return true;
        if (string.IsNullOrEmpty(password))
            return false;

        return string.Equals(PasswordHash, RelayService.HashPassword(password), StringComparison.OrdinalIgnoreCase);
    }
}

public class RelayService : IRelayService, IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, RelayRoom> _rooms = new(StringComparer.Ordinal);
    private readonly ILogger<RelayService> _logger;
    private readonly RelayOptions _options;
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _netManager;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _networkLoopTask;
    private readonly Timer? _cleanupTimer;
    private bool _disposed;

    public int Port => _netManager.LocalPort;

    public RelayService(ILogger<RelayService>? logger = null, IOptions<RelayOptions>? options = null)
    {
        _logger = logger ?? NullLogger<RelayService>.Instance;
        _options = options?.Value ?? new RelayOptions();

        _listener = new EventBasedNetListener();
        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.NetworkErrorEvent += OnNetworkError;

        _netManager = new NetManager(_listener)
        {
            AutoRecycle = true,
            UnconnectedMessagesEnabled = false
        };

        if (!_netManager.Start(_options.Port))
        {
            throw new InvalidOperationException($"Could not start Relay NetManager on port {_options.Port}");
        }

        _logger.LogInformation("RelayService started on UDP port {Port}", _netManager.LocalPort);

        _networkLoopTask = Task.Run(RunNetworkLoopAsync);
        _cleanupTimer = new Timer(_ => CleanupExpiredRooms(), null,
            TimeSpan.FromSeconds(_options.CleanupIntervalSeconds),
            TimeSpan.FromSeconds(_options.CleanupIntervalSeconds));
    }

    public static string HashPassword(string password)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    public Task<RelayResult<CreateRoomResponse>> CreateRoomAsync(
        string playerId,
        string roomName,
        string? password,
        string theme,
        int difficulty,
        string? playerName = null,
        CancellationToken cancellationToken = default)
    {
        string roomId = Guid.NewGuid().ToString("N");
        string hostToken = Guid.NewGuid().ToString("N");
        string guestToken = Guid.NewGuid().ToString("N");
        string? passwordHash = string.IsNullOrWhiteSpace(password) ? null : HashPassword(password.Trim());

        var room = new RelayRoom
        {
            RoomId = roomId,
            RoomName = roomName,
            HostPlayerId = playerId,
            HostPlayerName = !string.IsNullOrWhiteSpace(playerName) ? playerName : "Host",
            PasswordHash = passwordHash,
            Theme = theme,
            Difficulty = difficulty,
            Status = RelayRoomStatus.WaitingForGuest,
            HostToken = hostToken,
            GuestToken = guestToken,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        if (!_rooms.TryAdd(roomId, room))
        {
            return Task.FromResult(RelayResult<CreateRoomResponse>.Conflict("A room with this identifier already exists."));
        }

        var response = new CreateRoomResponse(
            RoomId: roomId,
            RoomName: roomName,
            Theme: theme,
            Difficulty: difficulty,
            RequiresPassword: room.RequiresPassword,
            RelayPort: Port,
            ConnectionToken: hostToken);

        return Task.FromResult(RelayResult<CreateRoomResponse>.Ok(response));
    }

    public Task<RelayResult<IReadOnlyList<RelayRoomSummary>>> ListRoomsAsync(
        CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromMinutes(_options.InactivityTimeoutMinutes);
        var unconnectedTimeout = TimeSpan.FromMinutes(_options.UnconnectedRoomTimeoutMinutes);

        var available = new List<RelayRoomSummary>();

        foreach (var room in _rooms.Values)
        {
            lock (room.SyncRoot)
            {
                if (room.Status != RelayRoomStatus.WaitingForGuest)
                    continue;

                if (room.IsClosed || room.IsExpired(timeout, unconnectedTimeout))
                    continue;

                if (room.GuestPlayerId != null)
                    continue;

                available.Add(new RelayRoomSummary(
                    RoomId: room.RoomId,
                    RoomName: room.RoomName,
                    HostPlayerName: room.HostPlayerName,
                    Theme: room.Theme,
                    Difficulty: room.Difficulty,
                    RequiresPassword: room.RequiresPassword,
                    CreatedAt: room.CreatedAt));
            }
        }

        return Task.FromResult(RelayResult<IReadOnlyList<RelayRoomSummary>>.Ok(available));
    }

    public Task<RelayResult<JoinRoomResponse>> JoinRoomAsync(
        string playerId,
        string roomId,
        string? password,
        string? playerName = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromMinutes(_options.InactivityTimeoutMinutes);
        var unconnectedTimeout = TimeSpan.FromMinutes(_options.UnconnectedRoomTimeoutMinutes);

        if (!_rooms.TryGetValue(roomId, out var room))
        {
            return Task.FromResult(RelayResult<JoinRoomResponse>.NotFound("Room does not exist."));
        }

        lock (room.SyncRoot)
        {
            if (room.IsClosed || room.IsExpired(timeout, unconnectedTimeout))
            {
                return Task.FromResult(RelayResult<JoinRoomResponse>.NotFound("Room is no longer available."));
            }

            if (room.GuestPlayerId != null || room.Status != RelayRoomStatus.WaitingForGuest)
            {
                return Task.FromResult(RelayResult<JoinRoomResponse>.Conflict("Room is already full."));
            }

            if (room.RequiresPassword && !room.VerifyPassword(password))
            {
                return Task.FromResult(RelayResult<JoinRoomResponse>.Unauthorized("Invalid password for this room."));
            }

            room.GuestPlayerId = playerId;
            room.GuestPlayerName = !string.IsNullOrWhiteSpace(playerName) ? playerName : "Guest";
            room.Status = RelayRoomStatus.Occupied;
            room.LastActivity = DateTime.UtcNow;

            var response = new JoinRoomResponse(
                RoomId: room.RoomId,
                RoomName: room.RoomName,
                Theme: room.Theme,
                Difficulty: room.Difficulty,
                RelayPort: Port,
                ConnectionToken: room.GuestToken);

            return Task.FromResult(RelayResult<JoinRoomResponse>.Ok(response));
        }
    }

    public Task<RelayResult> LeaveRoomAsync(
        string roomId,
        string playerId,
        CancellationToken cancellationToken = default)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
        {
            return Task.FromResult(RelayResult.NotFound("Room does not exist."));
        }

        lock (room.SyncRoot)
        {
            if (string.Equals(room.HostPlayerId, playerId, StringComparison.Ordinal))
            {
                room.Status = RelayRoomStatus.Closed;
                if (room.HostConnection is { ConnectionState: ConnectionState.Connected })
                    room.HostConnection.Disconnect();
                if (room.GuestConnection is { ConnectionState: ConnectionState.Connected })
                    room.GuestConnection.Disconnect();

                _rooms.TryRemove(roomId, out _);
                return Task.FromResult(RelayResult.Ok("Host left the room. Room closed."));
            }

            if (string.Equals(room.GuestPlayerId, playerId, StringComparison.Ordinal))
            {
                if (room.GuestConnection is { ConnectionState: ConnectionState.Connected })
                    room.GuestConnection.Disconnect();

                room.GuestConnection = null;
                room.GuestPlayerId = null;
                room.GuestPlayerName = null;
                room.Status = RelayRoomStatus.WaitingForGuest;
                room.LastActivity = DateTime.UtcNow;
                return Task.FromResult(RelayResult.Ok("Guest left the room."));
            }

            return Task.FromResult(RelayResult.NotFound("Player is not in this room."));
        }
    }

    public Task<RelayResult> CloseRoomAsync(
        string roomId,
        CancellationToken cancellationToken = default)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
        {
            return Task.FromResult(RelayResult.NotFound("Room does not exist."));
        }

        lock (room.SyncRoot)
        {
            room.Status = RelayRoomStatus.Closed;
            if (room.HostConnection is { ConnectionState: ConnectionState.Connected })
                room.HostConnection.Disconnect();
            if (room.GuestConnection is { ConnectionState: ConnectionState.Connected })
                room.GuestConnection.Disconnect();

            _rooms.TryRemove(roomId, out _);
        }

        return Task.FromResult(RelayResult.Ok("Room closed successfully."));
    }

    public void CleanupExpiredRooms()
    {
        var timeout = TimeSpan.FromMinutes(_options.InactivityTimeoutMinutes);
        var unconnectedTimeout = TimeSpan.FromMinutes(_options.UnconnectedRoomTimeoutMinutes);

        foreach (var pair in _rooms)
        {
            var room = pair.Value;
            bool shouldRemove = false;

            lock (room.SyncRoot)
            {
                if (room.IsClosed || room.IsExpired(timeout, unconnectedTimeout))
                {
                    room.Status = RelayRoomStatus.Closed;
                    if (room.HostConnection is { ConnectionState: ConnectionState.Connected })
                        room.HostConnection.Disconnect();
                    if (room.GuestConnection is { ConnectionState: ConnectionState.Connected })
                        room.GuestConnection.Disconnect();

                    shouldRemove = true;
                }
            }

            if (shouldRemove)
            {
                _rooms.TryRemove(pair.Key, out _);
                _logger.LogInformation("Expired room {RoomId} cleaned up", pair.Key);
            }
        }
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        try
        {
            if (request.Data.AvailableBytes <= 0)
            {
                _logger.LogWarning("Connection request rejected: empty data");
                request.Reject();
                return;
            }

            string rawKey = request.Data.GetString();
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                _logger.LogWarning("Connection request rejected: whitespace key");
                request.Reject();
                return;
            }

            if (!TryResolveRoomAndRole(rawKey, out var room, out bool isHost))
            {
                _logger.LogWarning("Connection request rejected: key could not be resolved to an active room. Key: {RawKey}", rawKey);
                request.Reject();
                return;
            }

            lock (room!.SyncRoot)
            {
                if (room.IsClosed)
                {
                    request.Reject();
                    return;
                }

                if (isHost)
                {
                    if (room.HostConnection is { ConnectionState: ConnectionState.Connected })
                    {
                        _logger.LogWarning("Rejecting duplicate Host connection for room {RoomId}", room.RoomId);
                        request.Reject();
                        return;
                    }

                    NetPeer peer = request.Accept();
                    peer.Tag = new RelayPeerSession(room.RoomId, isHost: true);
                    room.HostConnection = peer;
                    room.LastActivity = DateTime.UtcNow;
                    _logger.LogInformation("Host accepted for room {RoomId}, peer ID {PeerId}", room.RoomId, peer.Id);
                }
                else
                {
                    if (room.GuestConnection is { ConnectionState: ConnectionState.Connected })
                    {
                        _logger.LogWarning("Rejecting duplicate Guest connection for room {RoomId}", room.RoomId);
                        request.Reject();
                        return;
                    }

                    NetPeer peer = request.Accept();
                    peer.Tag = new RelayPeerSession(room.RoomId, isHost: false);
                    room.GuestConnection = peer;
                    room.LastActivity = DateTime.UtcNow;
                    _logger.LogInformation("Guest accepted for room {RoomId}, peer ID {PeerId}", room.RoomId, peer.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing connection request");
            request.Reject();
        }
    }

    private bool TryResolveRoomAndRole(string rawKey, out RelayRoom? room, out bool isHost)
    {
        room = null;
        isHost = false;

        string trimmed = rawKey.Trim();

        // 1. Try parsing JSON format
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                string? jsonRoomId = root.TryGetProperty("RoomId", out var r) || root.TryGetProperty("roomId", out r) ? r.GetString() : null;
                string? jsonToken = root.TryGetProperty("Token", out var t) || root.TryGetProperty("token", out t) ? t.GetString() : null;

                if (!string.IsNullOrEmpty(jsonRoomId) && _rooms.TryGetValue(jsonRoomId, out room))
                {
                    if (!string.IsNullOrEmpty(jsonToken))
                    {
                        if (string.Equals(room.HostToken, jsonToken, StringComparison.Ordinal))
                        {
                            isHost = true;
                            return true;
                        }
                        if (string.Equals(room.GuestToken, jsonToken, StringComparison.Ordinal))
                        {
                            isHost = false;
                            return true;
                        }
                    }
                    else
                    {
                        isHost = room.HostConnection == null;
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through
            }
        }

        // 2. Try "roomId:token" format
        int colonIndex = trimmed.IndexOf(':');
        if (colonIndex > 0)
        {
            string roomId = trimmed[..colonIndex];
            string token = trimmed[(colonIndex + 1)..];

            if (_rooms.TryGetValue(roomId, out room))
            {
                if (string.Equals(room.HostToken, token, StringComparison.Ordinal))
                {
                    isHost = true;
                    return true;
                }
                if (string.Equals(room.GuestToken, token, StringComparison.Ordinal))
                {
                    isHost = false;
                    return true;
                }
            }
        }

        // 3. Try matching token directly across active rooms
        foreach (var candidate in _rooms.Values)
        {
            if (string.Equals(candidate.HostToken, trimmed, StringComparison.Ordinal))
            {
                room = candidate;
                isHost = true;
                return true;
            }
            if (string.Equals(candidate.GuestToken, trimmed, StringComparison.Ordinal))
            {
                room = candidate;
                isHost = false;
                return true;
            }
        }

        // 4. Try matching roomId directly
        if (_rooms.TryGetValue(trimmed, out room))
        {
            isHost = room.HostConnection == null;
            return true;
        }

        return false;
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (peer.Tag is not RelayPeerSession session)
        {
            reader.Recycle();
            return;
        }

        if (!_rooms.TryGetValue(session.RoomId, out var room) || room.IsClosed)
        {
            reader.Recycle();
            return;
        }

        NetPeer? targetPeer;
        lock (room.SyncRoot)
        {
            if (room.IsClosed)
            {
                reader.Recycle();
                return;
            }

            targetPeer = session.IsHost ? room.GuestConnection : room.HostConnection;
            room.LastActivity = DateTime.UtcNow;
        }

        if (targetPeer is { ConnectionState: ConnectionState.Connected })
        {
            byte[] payload = reader.GetRemainingBytes();
            targetPeer.Send(payload, channel, deliveryMethod);
        }

        reader.Recycle();
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (peer.Tag is not RelayPeerSession session)
            return;

        if (!_rooms.TryGetValue(session.RoomId, out var room))
            return;

        lock (room.SyncRoot)
        {
            if (session.IsHost)
            {
                if (room.HostConnection == peer)
                    room.HostConnection = null;

                _logger.LogInformation("Host disconnected from room {RoomId}", room.RoomId);

                if (room.GuestConnection is { ConnectionState: ConnectionState.Connected })
                {
                    room.GuestConnection.Disconnect();
                    room.GuestConnection = null;
                }

                room.Status = RelayRoomStatus.Closed;
                _rooms.TryRemove(room.RoomId, out _);
            }
            else
            {
                if (room.GuestConnection == peer)
                    room.GuestConnection = null;

                _logger.LogInformation("Guest disconnected from room {RoomId}", room.RoomId);

                if (room.HostConnection is { ConnectionState: ConnectionState.Connected })
                {
                    room.HostConnection.Disconnect();
                    room.HostConnection = null;
                }

                room.Status = RelayRoomStatus.Closed;
                _rooms.TryRemove(room.RoomId, out _);
            }
        }
    }

    private void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
    {
        _logger.LogError("Relay network error on endpoint {EndPoint}: {SocketError}", endPoint, socketError);
    }

    private async Task RunNetworkLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _netManager.PollEvents();
                await Task.Delay(10, _cts.Token);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Relay network loop");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cleanupTimer?.Dispose();
        _cts.Cancel();
        try
        {
            await _networkLoopTask;
        }
        catch
        {
        }
        _cts.Dispose();

        foreach (var room in _rooms.Values)
        {
            lock (room.SyncRoot)
            {
                room.HostConnection?.Disconnect();
                room.GuestConnection?.Disconnect();
            }
        }
        _rooms.Clear();
        _netManager.Stop();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
