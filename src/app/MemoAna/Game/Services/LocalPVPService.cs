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
    private UdpClient? hostDiscoveryClient;
    private Task? acceptTask;
    private Task? receiveTask;
    private Task? advertiseTask;
    private Task? discoveryTask;
    private Task? hostDiscoveryTask;
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
        logger.LogInformation(
            "Local PVP host started. RoomId: {RoomId}, Player: {PlayerName}, TcpPort: {TcpPort}, DiscoveryPort: {DiscoveryPort}, HostAddress: {HostAddress}",
            RoomId, LocalPlayerName, GamePort, DiscoveryPort, GetLocalAddress());
        UdpClient discoverySocket = CreateDiscoverySocket(DiscoveryPort);
        hostDiscoveryClient = discoverySocket;
        logger.LogInformation("Local PVP UDP discovery listener started on port {DiscoveryPort}.", DiscoveryPort);
        acceptTask = AcceptPeerAsync(lifetimeCancellation.Token);
        hostDiscoveryTask = ListenForDiscoveryQueriesAsync(discoverySocket, lifetimeCancellation.Token);
        advertiseTask = AdvertiseRoomAsync(lifetimeCancellation.Token);
        logger.LogInformation("Local PVP room advertisement started for room {RoomId}.", RoomId);
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await StopDiscoveryAsync();
        discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryTask = DiscoverRoomsAsync(discoveryCancellation.Token);
        logger.LogInformation("Local PVP client discovery task started.");
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
        logger.LogInformation("Local PVP TCP connection attempt. RoomId: {RoomId}, HostAddress: {HostAddress}, TcpPort: {TcpPort}.", RoomId, room.HostAddress, room.TcpPort);
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
        hostDiscoveryClient?.Dispose();
        hostDiscoveryClient = null;
        await LeaveTransportAsync();
        await AwaitQuietly(acceptTask);
        await AwaitQuietly(advertiseTask);
        await AwaitQuietly(hostDiscoveryTask);
        await AwaitQuietly(receiveTask);
        cancellation?.Dispose();
        acceptTask = null;
        advertiseTask = null;
        hostDiscoveryTask = null;
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
        logger.LogInformation("Local PVP service stopped.");
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
            logger.LogInformation("Local PVP TCP peer accepted from {Endpoint}.", accepted.Client.RemoteEndPoint);
            await LeaveTransportAsync();
            client = accepted;
            stream = accepted.GetStream();
            receiveTask = ReceiveLoopAsync(cancellationToken);
            logger.LogInformation("Local PVP host receive loop started.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP TCP accept loop failed.");
            RaiseConnectionChanged(false, null, ex.Message);
        }
    }

    private static UdpClient CreateDiscoverySocket(int port)
    {
        UdpClient udp = new(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        return udp;
    }

    private async Task ListenForDiscoveryQueriesAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        logger.LogInformation("Local PVP UDP discovery listener running on {DiscoveryPort}.", DiscoveryPort);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(cancellationToken);
                if (!IsDiscoveryQuery(result.Buffer))
                    continue;

                logger.LogDebug(
                    "Local PVP discovery query received from {Endpoint}.",
                    result.RemoteEndPoint);
                byte[] response = CreateRoomAdvertisement();
                await udp.SendAsync(response, result.RemoteEndPoint);
                logger.LogDebug(
                    "Local PVP discovery response sent to {Endpoint} for room {RoomId}.",
                    result.RemoteEndPoint, RoomId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException ex) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Local PVP UDP discovery listener stopped during cancellation.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP UDP discovery listener failed on port {DiscoveryPort}.", DiscoveryPort);
        }
        finally
        {
            logger.LogInformation("Local PVP UDP discovery listener stopped on port {DiscoveryPort}.", DiscoveryPort);
        }
    }

    private bool IsDiscoveryQuery(byte[] payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("Protocol", out JsonElement protocol) &&
                string.Equals(protocol.GetString(), Protocol, StringComparison.Ordinal) &&
                root.TryGetProperty("Version", out JsonElement version) &&
                version.GetInt32() == ProtocolVersion &&
                root.TryGetProperty("Type", out JsonElement type) &&
                string.Equals(type.GetString(), "Query", StringComparison.Ordinal);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Ignoring malformed Local PVP discovery packet from the host listener.");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "Ignoring invalid Local PVP discovery packet from the host listener.");
            return false;
        }
        catch (FormatException ex)
        {
            logger.LogDebug(ex, "Ignoring invalid Local PVP discovery packet from the host listener.");
            return false;
        }
    }

    private byte[] CreateRoomAdvertisement() =>
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Protocol,
            Version = ProtocolVersion,
            Type = "Advertisement",
            RoomId,
            HostName = LocalPlayerName,
            HostIp = GetLocalAddress().ToString(),
            TcpPort = GamePort
        }, JsonOptions));

    private async Task DiscoverRoomsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Local PVP discovery started.");
        using UdpClient udp = new(AddressFamily.InterNetwork) { EnableBroadcast = true };

        logger.LogDebug("Local PVP discovery using UDP on {DiscoveryPort}.", DiscoveryPort);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        logger.LogDebug("Local PVP discovery bound to UDP endpoint {Endpoint}.", udp.Client.LocalEndPoint);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Local PVP discovery sending query to {BroadcastAddress}:{DiscoveryPort}.", BroadcastAddress, DiscoveryPort);
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
                        UdpReceiveResult result = await udp.ReceiveAsync(receiveTimeout.Token);
                        logger.LogTrace("Local PVP discovery response received from {Endpoint}.", result.RemoteEndPoint);
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
                byte[] advertisement = CreateRoomAdvertisement();
                logger.LogDebug("Local PVP sending room advertisement for {RoomId} to {BroadcastAddress}:{DiscoveryPort}.", RoomId, BroadcastAddress, DiscoveryPort);
                await udp.SendAsync(advertisement, new IPEndPoint(BroadcastAddress, DiscoveryPort));
                await Task.Delay(AdvertisementInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex) { logger.LogError(ex, "Local PVP advertisement error."); }
    }

    private async Task ProcessAdvertisementAsync(UdpReceiveResult result, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogTrace("Local PVP advertisement received from {Endpoint}.", result.RemoteEndPoint);
            using JsonDocument document = JsonDocument.Parse(result.Buffer);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("Protocol", out JsonElement protocol) ||
                !string.Equals(protocol.GetString(), Protocol, StringComparison.Ordinal) ||
                !root.TryGetProperty("Version", out JsonElement version) ||
                !version.TryGetInt32(out int protocolVersion) || protocolVersion != ProtocolVersion ||
                !root.TryGetProperty("Type", out JsonElement type) ||
                !string.Equals(type.GetString(), "Advertisement", StringComparison.Ordinal) ||
                !root.TryGetProperty("RoomId", out JsonElement roomIdElement) ||
                !root.TryGetProperty("HostName", out JsonElement hostNameElement) ||
                !root.TryGetProperty("TcpPort", out JsonElement tcpPortElement) ||
                roomIdElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(roomIdElement.GetString()) ||
                hostNameElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(hostNameElement.GetString()) ||
                !tcpPortElement.TryGetInt32(out int tcpPort) || tcpPort is < 1 or > 65535)
            {
                logger.LogDebug("Ignoring invalid Local PVP advertisement from {Endpoint}.", result.RemoteEndPoint);
                return;
            }

            string roomId = roomIdElement.GetString()!;
            string hostName = hostNameElement.GetString()!;
            string hostAddress = result.RemoteEndPoint.Address.ToString();
            LocalPvpRoom room = new(roomId, hostName, hostAddress, tcpPort, DateTime.UtcNow);
            bool changed;
            lock (stateLock)
            {
                changed = !rooms.TryGetValue(roomId, out LocalPvpRoom? old) ||
                    old.HostAddress != room.HostAddress || old.HostName != room.HostName || old.TcpPort != room.TcpPort;
                rooms[roomId] = room;
            }
            if (changed)
            {
                logger.LogInformation("Local PVP room discovered or updated. RoomId: {RoomId}, Host: {HostName}, Address: {HostAddress}:{TcpPort}.", room.RoomId, room.HostName, room.HostAddress, room.TcpPort);
                RoomsChanged?.Invoke(this, new(DiscoveredRooms));
            }
        }
        catch (JsonException ex) { logger.LogDebug(ex, "Ignoring malformed Local PVP advertisement from {Endpoint}.", result.RemoteEndPoint); }
        catch (InvalidOperationException ex) { logger.LogDebug(ex, "Ignoring invalid Local PVP advertisement from {Endpoint}.", result.RemoteEndPoint); }
        await Task.CompletedTask;
    }

    private void RemoveExpiredRooms()
    {
        DateTime threshold = DateTime.UtcNow - RoomExpiration;
        bool changed;
        lock (stateLock)
            changed = rooms.RemoveWhere(pair => pair.Value.LastSeenUtc < threshold) > 0;
        if (changed)
        {
            logger.LogDebug("Local PVP expired rooms removed.");
            RoomsChanged?.Invoke(this, new(DiscoveredRooms));
        }
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
                {
                    logger.LogWarning("Invalid Local PVP TCP frame length received: {FrameLength}.", length);
                    throw new InvalidDataException("Frame TCP inválido.");
                }
                byte[] payload = await ReadExactlyAsync(stream, length, cancellationToken);
                LocalPvpWireMessage? message = JsonSerializer.Deserialize<LocalPvpWireMessage>(payload, JsonOptions);
                if (message is null || message.Protocol != Protocol || message.Version != ProtocolVersion || message.RoomId != RoomId)
                {
                    logger.LogWarning("Ignoring invalid Local PVP TCP message. Protocol: {Protocol}, Version: {Version}, RoomId: {RoomId}.", message?.Protocol, message?.Version, message?.RoomId);
                    continue;
                }
                logger.LogDebug("Local PVP TCP message received. Type: {MessageType}.", message.Type);
                await HandleWireMessageAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (EndOfStreamException)
        {
            logger.LogInformation("Local PVP TCP peer disconnected.");
            RaiseConnectionChanged(false, RemotePlayerName, "O jogador adversário saiu.");
        }
        catch (InvalidDataException ex)
        {
            logger.LogError(ex, "Invalid Local PVP TCP frame.");
            RaiseConnectionChanged(false, RemotePlayerName, ex.Message);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Local PVP TCP receive error.");
            RaiseConnectionChanged(false, RemotePlayerName, ex.Message);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Local PVP TCP socket receive error.");
            RaiseConnectionChanged(false, RemotePlayerName, ex.Message);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Local PVP TCP message deserialization error.");
            RaiseConnectionChanged(false, RemotePlayerName, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Invalid Local PVP TCP message payload.");
            RaiseConnectionChanged(false, RemotePlayerName, ex.Message);
        }
        finally
        {
            logger.LogDebug("Local PVP TCP receive loop stopped.");
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
                logger.LogWarning("Local PVP handshake rejected for player {PlayerName}.", playerName);
                await SendWireMessageAsync(LocalPvpMessageType.HelloRejected, new { Reason = "Nome de usuário inválido ou já utilizado." }, cancellationToken);
                return;
            }

            RemotePlayerName = playerName.Trim();
            logger.LogInformation("Local PVP handshake accepted for remote player {RemotePlayerName}.", RemotePlayerName);
            await SendWireMessageAsync(LocalPvpMessageType.HelloAccepted, new { PlayerName = LocalPlayerName }, cancellationToken);
            peerConnected?.TrySetResult(true);
            RaiseConnectionChanged(true, RemotePlayerName);
            return;
        }

        if (message.Type == LocalPvpMessageType.HelloAccepted)
        {
            RemotePlayerName = message.Payload.GetProperty("PlayerName").GetString();
            logger.LogInformation("Local PVP handshake accepted by remote player {RemotePlayerName}.", RemotePlayerName);
            handshakeCompleted?.TrySetResult(true);
            RaiseConnectionChanged(true, RemotePlayerName);
            return;
        }

        if (message.Type == LocalPvpMessageType.HelloRejected)
        {
            string reason = message.Payload.GetProperty("Reason").GetString() ?? "Conexão rejeitada.";
            logger.LogWarning("Local PVP handshake rejected by host. Reason: {Reason}.", reason);
            handshakeCompleted?.TrySetException(new InvalidOperationException(reason));
            return;
        }

        LocalPvpMessageEventArgs args = new(message.Type, message.Payload);
        TaskCompletionSource<LocalPvpMessageEventArgs>? waiter = null;
        lock (stateLock)
            messageWaiters.Remove(message.Type, out waiter);
        waiter?.TrySetResult(args);
        logger.LogDebug("Local PVP game message dispatched. Type: {MessageType}.", message.Type);
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
            logger.LogDebug("Local PVP TCP message sent. Type: {MessageType}.", type);
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

    private static IPAddress GetLocalAddress()
    {
        IEnumerable<(int Priority, IPAddress Address)> candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address) &&
                    !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => (Priority: network.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => 0,
                    NetworkInterfaceType.Wireless80211 => 0,
                    _ => 1
                }, Address: address.Address)));

        return candidates.OrderBy(candidate => candidate.Priority).Select(candidate => candidate.Address).FirstOrDefault()
            ?? IPAddress.Loopback;
    }

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
