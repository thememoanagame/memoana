using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using LiteNetLib;
using LiteNetLib.Utils;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Models;
using MemoAna.Game.Protocol;

namespace MemoAna.Game.Services;

/// <summary>
/// Provides the Local PVP LAN session over one LiteNetLib UDP transport.
/// Network callbacks run on LiteNetLib's worker and only publish validated
/// game messages; UI dispatch remains the responsibility of the consumer.
/// </summary>
public sealed class LocalPVPService(
    ILogger<LocalPVPService> logger,
    LocalPvpOptions? options = null) : ILocalPVPService
{
    private const string DiscoveryMessage = "MemoAnaPvp.Discover";
    private const string DiscoveryResponse = "MemoAnaPvp.Room";
    private const string HandshakeKeyPrefix = "MemoAnaPvp/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LocalPvpOptions options = options ?? new();
    private readonly object stateLock = new();
    private readonly ConcurrentDictionary<LocalPvpMessageType, TaskCompletionSource<LocalPvpMessageEventArgs>> messageWaiters = [];
    private readonly Dictionary<string, LocalPvpRoom> rooms = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? lifetimeCancellation;
    private CancellationTokenSource? discoveryCancellation;
    private Task? discoveryTask;
    private EventBasedNetListener? listener;
    private NetManager? network;
    private NetPeer? peer;
    private TaskCompletionSource<bool>? peerConnected;
    private TaskCompletionSource<bool>? handshakeCompleted;
    private long nextSequence;
    private long lastReceivedSequence = -1;
    private string? handshakeKey;

    public bool IsHost { get; private set; }
    public bool IsConfigured { get; private set; }
    public bool IsConnected => peer?.ConnectionState == ConnectionState.Connected;
    public string? RoomId { get; private set; }
    public string? LocalPlayerName { get; private set; }
    public string? RemotePlayerName { get; private set; }

    public IReadOnlyList<LocalPvpRoom> DiscoveredRooms
    {
        get
        {
            lock (stateLock)
                return rooms.Values.ToList();
        }
    }

    public event EventHandler<LocalPvpRoomsChangedEventArgs>? RoomsChanged;
    public event EventHandler<LocalPvpConnectionEventArgs>? ConnectionChanged;
    public event EventHandler<LocalPvpMessageEventArgs>? MessageReceived;

    public async Task StartHostAsync(string playerName, CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizePlayerName(playerName);
        await LeaveAsync();
        lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Configure(true, normalizedName, Guid.CreateVersion7().ToString("N"));
        peerConnected = NewCompletionSource<bool>();
        StartNetwork();
        logger.LogInformation(
            "Local PVP transport started as host. RoomId: {RoomId}, Port: {Port}, Player: {PlayerName}.",
            RoomId, options.Port, LocalPlayerName);
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await StopDiscoveryAsync();
        await LeaveAsync();
        lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Configure(false, null, null);
        StartNetwork();

        CancellationTokenSource discoveryLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            lifetimeCancellation?.Token ?? cancellationToken);
        discoveryCancellation = discoveryLifetime;
        discoveryTask = DiscoverAsync(discoveryLifetime);
        logger.LogInformation("Local PVP discovery started on port {Port}.", options.Port);
    }

    public async Task ConnectAsync(
        LocalPvpRoom room,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        string normalizedName = NormalizePlayerName(playerName);
        await StopDiscoveryAsync();
        await LeaveTransportAsync();
        lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Configure(false, normalizedName, room.RoomId);
        StartNetwork();
        handshakeCompleted = NewCompletionSource<bool>();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        timeout.CancelAfter(options.ConnectionTimeout);
        handshakeKey = HandshakeKeyPrefix + room.RoomId;
        peer = network!.Connect(room.HostAddress, room.TcpPort, handshakeKey);
        if (peer is null)
            throw new InvalidOperationException("LiteNetLib did not create a connection attempt.");

        logger.LogInformation(
            "Local PVP connection requested. RoomId: {RoomId}, Peer: {Peer}, Port: {Port}.",
            room.RoomId, room.HostAddress, room.TcpPort);
        await handshakeCompleted.Task.WaitAsync(timeout.Token);
    }

    public async Task WaitForPeerAsync(CancellationToken cancellationToken = default)
    {
        if (!IsHost)
            throw new InvalidOperationException("Only the host can wait for a peer.");
        await (peerConnected ??= NewCompletionSource<bool>()).Task.WaitAsync(cancellationToken);
    }

    public async Task<LocalPvpMessageEventArgs> WaitForMessageAsync(
        LocalPvpMessageType messageType,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<LocalPvpMessageEventArgs> waiter = NewCompletionSource<LocalPvpMessageEventArgs>();
        if (!messageWaiters.TryAdd(messageType, waiter))
            throw new InvalidOperationException($"A waiter already exists for {messageType}.");
        try
        {
            return await waiter.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            messageWaiters.TryRemove(new KeyValuePair<LocalPvpMessageType, TaskCompletionSource<LocalPvpMessageEventArgs>>(messageType, waiter));
        }
    }

    public Task SendGameConfigurationAsync(LocalPvpGameConfiguration configuration, CancellationToken cancellationToken = default) =>
        SendAsync(LocalPvpMessageType.GameConfiguration, configuration, cancellationToken);

    public Task SendReadyAsync(CancellationToken cancellationToken = default) =>
        SendAsync(LocalPvpMessageType.GameReady, new { }, cancellationToken);

    public Task SendCardFlipAsync(LocalPvpCardFlip flip, CancellationToken cancellationToken = default) =>
        SendAsync(LocalPvpMessageType.CardFlip, flip, cancellationToken);

    public Task SendTurnResultAsync(LocalPvpTurnResult result, CancellationToken cancellationToken = default) =>
        SendAsync(LocalPvpMessageType.TurnResult, result, cancellationToken);

    public Task SendGameFinishedAsync(LocalPvpTurnResult result, CancellationToken cancellationToken = default) =>
        SendAsync(LocalPvpMessageType.GameFinished, result, cancellationToken);

    public async Task LeaveAsync()
    {
        CancellationTokenSource? cancellation = lifetimeCancellation;
        lifetimeCancellation = null;
        cancellation?.Cancel();
        await StopDiscoveryAsync();
        await LeaveTransportAsync();
        cancellation?.Dispose();
        lock (stateLock)
            rooms.Clear();
        IsHost = false;
        IsConfigured = false;
        RoomId = null;
        LocalPlayerName = null;
        RemotePlayerName = null;
        peerConnected = null;
        handshakeCompleted = null;
        logger.LogInformation("Local PVP transport stopped.");
    }

    public async ValueTask DisposeAsync() => await LeaveAsync();

    private void Configure(bool isHost, string? playerName, string? roomId)
    {
        IsHost = isHost;
        IsConfigured = true;
        LocalPlayerName = playerName;
        RoomId = roomId;
        nextSequence = 0;
        lastReceivedSequence = -1;
        handshakeKey = roomId is null ? null : HandshakeKeyPrefix + roomId;
    }

    private void StartNetwork()
    {
        listener = new EventBasedNetListener();
        listener.ConnectionRequestEvent += OnConnectionRequest;
        listener.PeerConnectedEvent += OnPeerConnected;
        listener.PeerDisconnectedEvent += OnPeerDisconnected;
        listener.NetworkReceiveEvent += OnNetworkReceive;
        listener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;
        listener.NetworkErrorEvent += OnNetworkError;
        network = new NetManager(listener)
        {
            UnconnectedMessagesEnabled = true,
            BroadcastReceiveEnabled = true,
            ReuseAddress = true,
            DisconnectTimeout = 15_000
        };
        if (!network.Start(options.Port))
            throw new InvalidOperationException($"Unable to start Local PVP on UDP port {options.Port}.");
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        if (!IsHost || string.IsNullOrWhiteSpace(handshakeKey))
        {
            request.Reject();
            return;
        }

        peer = request.AcceptIfKey(handshakeKey);
        if (peer is null)
            logger.LogWarning("Local PVP rejected a peer with an invalid handshake key.");
        else
            logger.LogInformation("Local PVP peer connection accepted from {Endpoint}.", request.RemoteEndPoint);
    }

    private void OnPeerConnected(NetPeer connectedPeer)
    {
        peer = connectedPeer;
        peerConnected?.TrySetResult(true);
        logger.LogInformation(
            "Local PVP peer connected: {Address}:{Port}.",
            connectedPeer.Address,
            connectedPeer.Port);
        if (!IsHost)
            _ = SendAsync(LocalPvpMessageType.Hello, new { PlayerName = LocalPlayerName }, CancellationToken.None);
    }

    private void OnPeerDisconnected(NetPeer disconnectedPeer, DisconnectInfo disconnectInfo)
    {
        if (ReferenceEquals(peer, disconnectedPeer))
            peer = null;
        string reason = disconnectInfo.Reason.ToString();
        logger.LogInformation("Local PVP peer disconnected. Reason: {Reason}.", reason);
        handshakeCompleted?.TrySetException(new IOException($"The PVP peer disconnected: {reason}."));
        RaiseConnectionChanged(false, RemotePlayerName, "The opponent disconnected.");
    }

    private void OnNetworkReceive(
        NetPeer sender,
        NetPacketReader reader,
        byte channel,
        DeliveryMethod deliveryMethod)
    {
        try
        {
            string json = reader.GetString();
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            if (!LocalPvpProtocol.TryDeserialize(bytes, RoomId ?? string.Empty, out LocalPvpMessage? message))
            {
                logger.LogWarning(
                    "Invalid Local PVP message received from {Address}:{Port}.",
                    sender.Address,
                    sender.Port);
                return;
            }

            if (message.Sequence <= lastReceivedSequence)
            {
                logger.LogDebug("Ignoring duplicate or out-of-order Local PVP message {MessageId}.", message.MessageId);
                return;
            }
            lastReceivedSequence = message.Sequence;
            HandleMessage(message);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(
                ex,
                "Invalid Local PVP payload received from {Address}:{Port}.",
                sender.Address,
                sender.Port);
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void HandleMessage(LocalPvpMessage message)
    {
        if (message.MessageType == LocalPvpMessageType.Hello)
        {
            string playerName = message.Payload.GetProperty("PlayerName").GetString() ?? string.Empty;
            if (!IsHost || string.Equals(playerName, LocalPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Local PVP handshake rejected for player {PlayerName}.", playerName);
                _ = SendAsync(LocalPvpMessageType.HelloRejected, new { Reason = "Invalid or duplicate player name." }, CancellationToken.None);
                return;
            }

            RemotePlayerName = NormalizePlayerName(playerName);
            _ = SendAsync(LocalPvpMessageType.HelloAccepted, new { PlayerName = LocalPlayerName }, CancellationToken.None);
            peerConnected?.TrySetResult(true);
            RaiseConnectionChanged(true, RemotePlayerName);
            logger.LogInformation("Local PVP handshake completed with {PlayerName}.", RemotePlayerName);
            return;
        }

        if (message.MessageType == LocalPvpMessageType.HelloAccepted)
        {
            RemotePlayerName = message.Payload.GetProperty("PlayerName").GetString();
            handshakeCompleted?.TrySetResult(true);
            RaiseConnectionChanged(true, RemotePlayerName);
            logger.LogInformation("Local PVP handshake completed with {PlayerName}.", RemotePlayerName);
            return;
        }

        if (message.MessageType == LocalPvpMessageType.HelloRejected)
        {
            string reason = message.Payload.GetProperty("Reason").GetString() ?? "Connection rejected.";
            handshakeCompleted?.TrySetException(new InvalidOperationException(reason));
            return;
        }

        LocalPvpMessageEventArgs args = new(message.MessageType, message.Payload);
        if (messageWaiters.TryRemove(message.MessageType, out TaskCompletionSource<LocalPvpMessageEventArgs>? waiter))
            waiter.TrySetResult(args);
        MessageReceived?.Invoke(this, args);
        logger.LogDebug("Local PVP message received. Type: {MessageType}.", message.MessageType);
    }

    private Task SendAsync(LocalPvpMessageType type, object payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NetPeer currentPeer = peer ?? throw new InvalidOperationException("The Local PVP peer is not connected.");
        string roomId = RoomId ?? throw new InvalidOperationException("The Local PVP room is not configured.");
        byte[] bytes = LocalPvpProtocol.Serialize(type, roomId, Interlocked.Increment(ref nextSequence), payload);
        NetDataWriter writer = new();
        writer.Put(System.Text.Encoding.UTF8.GetString(bytes));
        currentPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        return Task.CompletedTask;
    }

    private async Task DiscoverAsync(CancellationTokenSource discoveryLifetime)
    {
        try
        {
            while (!discoveryLifetime.IsCancellationRequested)
            {
                NetDataWriter writer = new();
                writer.Put(DiscoveryMessage);
                network?.SendUnconnectedMessage(writer, "255.255.255.255", options.Port);
                await Task.Delay(options.DiscoveryInterval, discoveryLifetime.Token);
            }
        }
        catch (OperationCanceledException) when (discoveryLifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP discovery failed.");
            RaiseConnectionChanged(false, null, ex.Message);
        }
    }

    private void OnNetworkReceiveUnconnected(
        IPEndPoint remoteEndPoint,
        NetPacketReader reader,
        UnconnectedMessageType messageType)
    {
        try
        {
            string message = reader.GetString();
            if (IsHost && string.Equals(message, DiscoveryMessage, StringComparison.Ordinal))
            {
                NetDataWriter writer = new();
                writer.Put(DiscoveryResponse);
                writer.Put(RoomId);
                writer.Put(LocalPlayerName);
                writer.Put(options.Port);
                network?.SendUnconnectedMessage(writer, remoteEndPoint);
                logger.LogDebug("Local PVP room advertisement sent to {Endpoint}.", remoteEndPoint);
                return;
            }

            if (!IsHost && string.Equals(message, DiscoveryResponse, StringComparison.Ordinal))
            {
                string roomId = reader.GetString();
                string hostName = reader.GetString();
                int port = reader.GetInt();
                if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(hostName) || port is < 1 or > 65535)
                    return;

                LocalPvpRoom room = new(roomId, hostName, remoteEndPoint.Address.ToString(), port, DateTime.UtcNow);
                bool changed;
                lock (stateLock)
                {
                    changed = !rooms.TryGetValue(room.RoomId, out LocalPvpRoom? previous) ||
                        previous.HostName != room.HostName ||
                        previous.HostAddress != room.HostAddress ||
                        previous.TcpPort != room.TcpPort;
                    rooms[room.RoomId] = room;
                }

                if (changed)
                {
                    IReadOnlyList<LocalPvpRoom> snapshot = DiscoveredRooms;
                    RoomsChanged?.Invoke(this, new(snapshot));
                    logger.LogInformation(
                        "Local PVP peer discovered. RoomId: {RoomId}, Host: {HostName}, Endpoint: {HostAddress}:{Port}.",
                        room.RoomId, room.HostName, room.HostAddress, room.TcpPort);
                }
            }
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) =>
        logger.LogWarning("Local PVP network error at {Endpoint}: {SocketError}.", endPoint, socketError);

    private async Task StopDiscoveryAsync()
    {
        CancellationTokenSource? cancellation = discoveryCancellation;
        discoveryCancellation = null;
        cancellation?.Cancel();
        if (discoveryTask is not null)
        {
            try { await discoveryTask; }
            catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true) { }
        }
        discoveryTask = null;
        cancellation?.Dispose();
    }

    private async Task LeaveTransportAsync()
    {
        peer?.Disconnect();
        peer = null;
        network?.Stop();
        network = null;
        listener = null;
        await Task.CompletedTask;
    }

    private void RaiseConnectionChanged(bool connected, string? playerName, string? error = null) =>
        ConnectionChanged?.Invoke(this, new(connected, playerName, error));

    private static string NormalizePlayerName(string value)
    {
        string name = value.Trim();
        if (name.Length is < 1 or > 24)
            throw new ArgumentException("The player name must contain between 1 and 24 characters.", nameof(value));
        return name;
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
