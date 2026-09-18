using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Models;

namespace MemoAna.Game.Services;

/// <summary>
/// Provides direct LAN discovery and framed TCP transport for one local PVP
/// room. The service owns all sockets and exposes only game-level messages.
/// </summary>
public sealed class LocalPVPService(ILogger<LocalPVPService> logger) : ILocalPVPService
{
    private const string Protocol = "MemoAnaPvp";
    private const int ProtocolVersion = 1;
    private const int DiscoveryPort = 45873;
    private const int GamePort = 45874;
    private const int FrameHeaderSize = 4;
    private const int MaxFrameSize = 1024 * 1024;
    private static readonly TimeSpan AdvertisementInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RoomExpiration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly IPAddress BroadcastAddress = IPAddress.Parse("255.255.255.255");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object stateLock = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Dictionary<string, LocalPvpRoom> rooms = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? lifetimeCancellation;
    private CancellationTokenSource? discoveryCancellation;
    private TcpListener? listener;
    private TcpClient? client;
    private NetworkStream? stream;
    private Task? acceptTask;
    private Task? receiveTask;
    private Task? advertiseTask;
    private Task? discoveryTask;
    private TaskCompletionSource<bool>? peerConnected;
    private TaskCompletionSource<bool>? handshakeCompleted;
    private readonly Dictionary<LocalPvpMessageType, TaskCompletionSource<LocalPvpMessageEventArgs>> messageWaiters = [];

    public bool IsHost { get; private set; }
    public bool IsConfigured { get; private set; }
    public bool IsConnected => stream is not null && client?.Connected == true;
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
        IsHost = true;
        IsConfigured = true;
        LocalPlayerName = normalizedName;
        RoomId = Guid.CreateVersion7().ToString("N");
        peerConnected = NewCompletionSource();

        listener = new TcpListener(IPAddress.Any, GamePort);
        listener.Start();
        acceptTask = AcceptPeerAsync(lifetimeCancellation.Token);
        advertiseTask = AdvertiseRoomAsync(lifetimeCancellation.Token);
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await StopDiscoveryAsync();
        discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryTask = DiscoverRoomsAsync(discoveryCancellation.Token);
    }

    public async Task ConnectAsync(LocalPvpRoom room, string playerName, CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizePlayerName(playerName);
        await StopDiscoveryAsync();
        await LeaveTransportAsync();

        lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsHost = false;
        IsConfigured = true;
        LocalPlayerName = normalizedName;
        RoomId = room.RoomId;
        logger.LogInformation("Local PVP client connected to room {RoomId}.", RoomId);
        handshakeCompleted = NewCompletionSource();
        logger.LogInformation("Local PVP client sending handshake to {HostAddress}:{TcpPort}.", room.HostAddress, room.TcpPort);
        client = new TcpClient();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        timeout.CancelAfter(ConnectionTimeout);
        logger.LogInformation("Local PVP client waiting for connection to {HostAddress}:{TcpPort}.", room.HostAddress, room.TcpPort);
        await client.ConnectAsync(IPAddress.Parse(room.HostAddress), room.TcpPort, timeout.Token);
        logger.LogInformation("Local PVP client connected to {HostAddress}:{TcpPort}.", room.HostAddress, room.TcpPort);
        stream = client.GetStream();
        logger.LogInformation("Local PVP client starting receive loop.");
        receiveTask = ReceiveLoopAsync(lifetimeCancellation.Token);
        logger.LogInformation("Local PVP client sending handshake message.");
        await SendWireMessageAsync(LocalPvpMessageType.Hello, new { PlayerName = normalizedName }, timeout.Token);
        logger.LogInformation("Local PVP client waiting for handshake response.");
        await handshakeCompleted.Task.WaitAsync(timeout.Token);
        logger.LogInformation("Local PVP client handshake completed with remote player {RemotePlayerName}.", RemotePlayerName);
    }

    public async Task WaitForPeerAsync(CancellationToken cancellationToken = default)
    {
        if (!IsHost)
            throw new InvalidOperationException("Somente o host aguarda o segundo jogador.");

        peerConnected ??= NewCompletionSource();
        await peerConnected.Task.WaitAsync(cancellationToken);
    }

    public async Task<LocalPvpMessageEventArgs> WaitForMessageAsync(LocalPvpMessageType messageType, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<LocalPvpMessageEventArgs> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (stateLock)
            messageWaiters[messageType] = waiter;
        try
        {
            return await waiter.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (stateLock)
            {
                if (messageWaiters.TryGetValue(messageType, out TaskCompletionSource<LocalPvpMessageEventArgs>? current) && ReferenceEquals(current, waiter))
                    messageWaiters.Remove(messageType);
            }
        }
    }

    public Task SendGameConfigurationAsync(LocalPvpGameConfiguration configuration, CancellationToken cancellationToken = default) =>
        SendWireMessageAsync(LocalPvpMessageType.GameConfiguration, configuration, cancellationToken);

    public Task SendReadyAsync(CancellationToken cancellationToken = default) =>
        SendWireMessageAsync(LocalPvpMessageType.GameReady, new { }, cancellationToken);

    public Task SendCardFlipAsync(LocalPvpCardFlip flip, CancellationToken cancellationToken = default) =>
        SendWireMessageAsync(LocalPvpMessageType.CardFlip, flip, cancellationToken);

    public Task SendTurnResultAsync(LocalPvpTurnResult result, CancellationToken cancellationToken = default) =>
        SendWireMessageAsync(LocalPvpMessageType.TurnResult, result, cancellationToken);

    public Task SendGameFinishedAsync(LocalPvpTurnResult result, CancellationToken cancellationToken = default) =>
        SendWireMessageAsync(LocalPvpMessageType.GameFinished, result, cancellationToken);

    public async Task LeaveAsync()
    {
        CancellationTokenSource? cancellation = lifetimeCancellation;
        lifetimeCancellation = null;
        cancellation?.Cancel();
        await StopDiscoveryAsync();
        listener?.Stop();
        listener = null;
        await LeaveTransportAsync();
        await AwaitQuietly(acceptTask);
        await AwaitQuietly(advertiseTask);
        await AwaitQuietly(receiveTask);
        cancellation?.Dispose();
        acceptTask = null;
        advertiseTask = null;
        receiveTask = null;
        lock (stateLock)
            rooms.Clear();
        IsHost = false;
        IsConfigured = false;
        RoomId = null;
        LocalPlayerName = null;
        RemotePlayerName = null;
        peerConnected = null;
        handshakeCompleted = null;
    }

    public async ValueTask DisposeAsync()
    {
        await LeaveAsync();
        sendLock.Dispose();
    }

    private async Task AcceptPeerAsync(CancellationToken cancellationToken)
    {
        try
        {
            TcpClient accepted = await listener!.AcceptTcpClientAsync(cancellationToken);
            await LeaveTransportAsync();
            client = accepted;
            stream = accepted.GetStream();
            receiveTask = ReceiveLoopAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            RaiseConnectionChanged(false, null, ex.Message);
        }
    }

    private async Task DiscoverRoomsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Local PVP discovery started.");
        using UdpClient udp = new(AddressFamily.InterNetwork) { EnableBroadcast = true };

        logger.LogInformation("Local PVP discovery using UDP on {DiscoveryPort}.", DiscoveryPort);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        logger.LogInformation("Local PVP discovery bound to UDP socket.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Local PVP discovery sending query.");
                byte[] query = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    Protocol,
                    Version = ProtocolVersion,
                    Type = "Query"
                }, JsonOptions));
                await udp.SendAsync(query, new IPEndPoint(BroadcastAddress, DiscoveryPort));
                using CancellationTokenSource receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveTimeout.CancelAfter(500);
                try
                {
                    while (!receiveTimeout.IsCancellationRequested)
                    {
                        logger.LogInformation("Local PVP discovery waiting for response.");
                        UdpReceiveResult result = await udp.ReceiveAsync(receiveTimeout.Token);
                        await ProcessAdvertisementAsync(result, cancellationToken);
                    }
                }
                catch (OperationCanceledException) { }

                RemoveExpiredRooms();
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP discovery error: {ErrorMessage}", ex.Message);
            RaiseConnectionChanged(false, null, ex.Message);
        }
    }

    private async Task AdvertiseRoomAsync(CancellationToken cancellationToken)
    {
        using UdpClient udp = new() { EnableBroadcast = true };
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Local PVP advertising room {RoomId} on UDP port {DiscoveryPort}.", RoomId, DiscoveryPort);
                byte[] advertisement = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    Protocol,
                    Version = ProtocolVersion,
                    Type = "Advertisement",
                    RoomId,
                    HostName = LocalPlayerName,
                    HostIp = GetLocalAddress().ToString(),
                    TcpPort = GamePort
                }, JsonOptions));
                logger.LogInformation("Local PVP sending advertisement to {BroadcastAddress}:{DiscoveryPort}.", BroadcastAddress, DiscoveryPort);
                await udp.SendAsync(advertisement, new IPEndPoint(BroadcastAddress, DiscoveryPort));
                await Task.Delay(AdvertisementInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException se) { logger.LogError("Local PVP advertisement error: {ErrorMessage}", se.Message); }
    }

    private async Task ProcessAdvertisementAsync(UdpReceiveResult result, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Buffer);
            JsonElement root = document.RootElement;
            if (!string.Equals(root.GetProperty("Protocol").GetString(), Protocol, StringComparison.Ordinal) ||
                root.GetProperty("Version").GetInt32() != ProtocolVersion ||
                !string.Equals(root.GetProperty("Type").GetString(), "Advertisement", StringComparison.Ordinal))
                return;

            string roomId = root.GetProperty("RoomId").GetString()!;
            string hostName = root.GetProperty("HostName").GetString()!;
            int tcpPort = root.GetProperty("TcpPort").GetInt32();
            string hostAddress = result.RemoteEndPoint.Address.ToString();
            LocalPvpRoom room = new(roomId, hostName, hostAddress, tcpPort, DateTime.UtcNow);
            bool changed;
            lock (stateLock)
            {
                changed = !rooms.TryGetValue(roomId, out LocalPvpRoom? old) || old.HostAddress != room.HostAddress || old.LastSeenUtc != room.LastSeenUtc;
                rooms[roomId] = room;
            }
            if (changed)
                RoomsChanged?.Invoke(this, new(DiscoveredRooms));
        }
        catch (JsonException) { }
        catch (KeyNotFoundException) { }
        await Task.CompletedTask;
    }

    private void RemoveExpiredRooms()
    {
        DateTime threshold = DateTime.UtcNow - RoomExpiration;
        bool changed;
        lock (stateLock)
            changed = rooms.RemoveWhere(pair => pair.Value.LastSeenUtc < threshold) > 0;
        if (changed)
            RoomsChanged?.Invoke(this, new(DiscoveredRooms));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && stream is not null)
            {
                byte[] header = await ReadExactlyAsync(stream, FrameHeaderSize, cancellationToken);
                int length = BinaryPrimitives.ReadInt32BigEndian(header);
                if (length <= 0 || length > MaxFrameSize)
                    throw new InvalidDataException("Frame TCP inválido.");
                byte[] payload = await ReadExactlyAsync(stream, length, cancellationToken);
                LocalPvpWireMessage? message = JsonSerializer.Deserialize<LocalPvpWireMessage>(payload, JsonOptions);
                if (message is null || message.Protocol != Protocol || message.Version != ProtocolVersion || message.RoomId != RoomId)
                    continue;
                await HandleWireMessageAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { RaiseConnectionChanged(false, RemotePlayerName, "O jogador adversário saiu."); }
        catch (IOException ex) { RaiseConnectionChanged(false, RemotePlayerName, ex.Message); }
        catch (SocketException ex) { RaiseConnectionChanged(false, RemotePlayerName, ex.Message); }
        finally
        {
            if (IsConfigured && cancellationToken.IsCancellationRequested == false)
                RaiseConnectionChanged(false, RemotePlayerName, "Conexão encerrada.");
        }
    }

    private async Task HandleWireMessageAsync(LocalPvpWireMessage message, CancellationToken cancellationToken)
    {
        if (message.Type == LocalPvpMessageType.Hello)
        {
            string playerName = message.Payload.GetProperty("PlayerName").GetString() ?? string.Empty;
            if (!IsHost || string.Equals(playerName, LocalPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                await SendWireMessageAsync(LocalPvpMessageType.HelloRejected, new { Reason = "Nome de usuário inválido ou já utilizado." }, cancellationToken);
                return;
            }

            RemotePlayerName = playerName.Trim();
            await SendWireMessageAsync(LocalPvpMessageType.HelloAccepted, new { PlayerName = LocalPlayerName }, cancellationToken);
            peerConnected?.TrySetResult(true);
            RaiseConnectionChanged(true, RemotePlayerName);
            return;
        }

        if (message.Type == LocalPvpMessageType.HelloAccepted)
        {
            RemotePlayerName = message.Payload.GetProperty("PlayerName").GetString();
            handshakeCompleted?.TrySetResult(true);
            RaiseConnectionChanged(true, RemotePlayerName);
            return;
        }

        if (message.Type == LocalPvpMessageType.HelloRejected)
        {
            string reason = message.Payload.GetProperty("Reason").GetString() ?? "Conexão rejeitada.";
            handshakeCompleted?.TrySetException(new InvalidOperationException(reason));
            return;
        }

        LocalPvpMessageEventArgs args = new(message.Type, message.Payload);
        lock (stateLock)
        {
            if (messageWaiters.Remove(message.Type, out TaskCompletionSource<LocalPvpMessageEventArgs>? waiter))
                waiter.TrySetResult(args);
        }
        MessageReceived?.Invoke(this, args);
        await Task.CompletedTask;
    }

    private async Task SendWireMessageAsync(LocalPvpMessageType type, object payload, CancellationToken cancellationToken)
    {
        NetworkStream currentStream = stream ?? throw new InvalidOperationException("A conexão PVP não está estabelecida.");
        LocalPvpWireMessage message = new(Protocol, ProtocolVersion, type, RoomId ?? string.Empty,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        byte[] frame = new byte[FrameHeaderSize + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, FrameHeaderSize), body.Length);
        body.CopyTo(frame, FrameHeaderSize);
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await currentStream.WriteAsync(frame, cancellationToken);
            await currentStream.FlushAsync(cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task LeaveTransportAsync()
    {
        stream?.Close();
        stream?.Dispose();
        stream = null;
        client?.Close();
        client?.Dispose();
        client = null;
        await AwaitQuietly(receiveTask);
        receiveTask = null;
    }

    private async Task StopDiscoveryAsync()
    {
        discoveryCancellation?.Cancel();
        await AwaitQuietly(discoveryTask);
        discoveryCancellation?.Dispose();
        discoveryCancellation = null;
        discoveryTask = null;
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream source, int length, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await source.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
        return buffer;
    }

    private static string NormalizePlayerName(string value)
    {
        string name = value.Trim();
        if (name.Length is < 1 or > 24)
            throw new ArgumentException("O nome deve possuir entre 1 e 24 caracteres.", nameof(value));
        return name;
    }

    private static IPAddress GetLocalAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
        ?? IPAddress.Loopback;

    private void RaiseConnectionChanged(bool connected, string? playerName, string? error = null) =>
        ConnectionChanged?.Invoke(this, new(connected, playerName, error));

    private static TaskCompletionSource<bool> NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task AwaitQuietly(Task? task)
    {
        if (task is null) return;
        try { await task; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
    }

    private sealed record LocalPvpWireMessage(
        string Protocol,
        int Version,
        LocalPvpMessageType Type,
        string RoomId,
        JsonElement Payload);
}

internal static class DictionaryExtensions
{
    public static int RemoveWhere<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, Func<KeyValuePair<TKey, TValue>, bool> predicate)
    {
        List<TKey> keys = dictionary.Where(predicate).Select(pair => pair.Key).ToList();
        foreach (TKey key in keys)
            dictionary.Remove(key);
        return keys.Count;
    }
}
