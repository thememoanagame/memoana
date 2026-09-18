using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
    private const int DefaultGamePort = 45874;
    private const int DiscoveryPort = 45873;
    private const int GamePort = DefaultGamePort;
    private const int FrameHeaderSize = 4;
    private const int MaxFrameSize = 1024 * 1024;
    private static readonly TimeSpan AdvertisementInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RoomExpiration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object stateLock = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Dictionary<string, LocalPvpRoom> rooms = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? lifetimeCancellation;
    private CancellationTokenSource? discoveryCancellation;
    private TcpListener? listener;
    private TcpClient? client;
    private NetworkStream? stream;
    private LocalPvpMdnsDiscovery? mdns;
    private Task? acceptTask;
    private Task? receiveTask;
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
        logger.LogInformation("Local PVP host starting with player name {PlayerName}.", playerName);
        string normalizedName = NormalizePlayerName(playerName);
        await LeaveAsync();
        logger.LogInformation("Local PVP host configuration cleared.");
        lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsHost = true;
        IsConfigured = true;
        LocalPlayerName = normalizedName;
        RoomId = Guid.CreateVersion7().ToString("N");
        peerConnected = NewCompletionSource();

        try
        {
            logger.LogInformation("Local PVP TCP listener starting on wildcard port {TcpPort}.", DefaultGamePort);
            listener = new TcpListener(IPAddress.IPv6Any, DefaultGamePort);
            listener.Server.DualMode = true;
            listener.Start();
            logger.LogInformation("Local PVP TCP listener bound to {TcpEndpoint}.", listener.LocalEndpoint);
            int tcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            logger.LogInformation(
                "Local PVP host started. RoomId: {RoomId}, Player: {PlayerName}, TcpPort: {TcpPort}",
                RoomId, LocalPlayerName, tcpPort);
            mdns = new LocalPvpMdnsDiscovery(logger);
            mdns.StartAdvertisement(RoomId, LocalPlayerName, tcpPort, ProtocolVersion);
            acceptTask = AcceptPeerAsync(lifetimeCancellation.Token);
            logger.LogInformation("Local PVP room advertisement started for room {RoomId}.", RoomId);
        }
        catch
        {
            await LeaveAsync();
            throw;
        }
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        await StopDiscoveryAsync();
        CancellationTokenSource discoveryLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryCancellation = discoveryLifetime;
        mdns = new LocalPvpMdnsDiscovery(logger);
        mdns.RoomDiscovered += OnMdnsRoomDiscovered;
        mdns.RoomRemoved += OnMdnsRoomRemoved;
        try
        {
            mdns.StartBrowsing();
            logger.LogInformation("Local PVP client discovery started.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP mDNS discovery failed to start.");
            RaiseConnectionChanged(false, null, ex.Message);
            await StopDiscoveryAsync();
        }
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
        logger.LogInformation("Local PVP TCP connection requested. RoomId: {RoomId}, HostAddress: {HostAddress}, TcpPort: {TcpPort}.", RoomId, room.HostAddress, room.TcpPort);
        handshakeCompleted = NewCompletionSource();
        client = new TcpClient();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        timeout.CancelAfter(ConnectionTimeout);
        logger.LogInformation(
            "Local PVP TCP ConnectAsync starting to {HostAddress}:{TcpPort}. An Android emulator 10.0.2.x endpoint is behind emulator NAT and is not inbound-reachable from a physical device.",
            room.HostAddress, room.TcpPort);
        IPAddress[] addresses = IPAddress.TryParse(room.HostAddress, out IPAddress? parsed)
            ? [parsed]
            : await Dns.GetHostAddressesAsync(room.HostAddress, timeout.Token);
        await client.ConnectAsync(addresses, room.TcpPort, timeout.Token);
        logger.LogInformation(
            "Local PVP TCP connected. LocalEndpoint: {LocalEndpoint}, RemoteEndpoint: {RemoteEndpoint}.",
            client.Client.LocalEndPoint, client.Client.RemoteEndPoint);
        stream = client.GetStream();
        logger.LogInformation("Local PVP TCP receive loop started.");
        receiveTask = ReceiveLoopAsync(lifetimeCancellation.Token);
        logger.LogInformation("Local PVP sending Hello.");
        await SendWireMessageAsync(LocalPvpMessageType.Hello, new { PlayerName = normalizedName }, timeout.Token);
        logger.LogInformation("Local PVP Hello sent. Waiting for HelloAccepted.");
        await handshakeCompleted.Task.WaitAsync(timeout.Token);
        logger.LogInformation("Local PVP handshake completed. RemotePlayerName: {RemotePlayerName}.", RemotePlayerName);
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
        mdns?.Stop();
        mdns?.Dispose();
        mdns = null;
        await LeaveTransportAsync();
        await AwaitQuietly(acceptTask);
        await AwaitQuietly(receiveTask);
        cancellation?.Dispose();
        acceptTask = null;
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
            logger.LogInformation("Local PVP TCP listener accepted peer. RemoteEndpoint: {RemoteEndpoint}.", accepted.Client.RemoteEndPoint);
            await LeaveTransportAsync();
            client = accepted;
            stream = accepted.GetStream();
            receiveTask = ReceiveLoopAsync(cancellationToken);
            logger.LogInformation("Local PVP TCP receive loop started.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP TCP accept loop failed.");
            RaiseConnectionChanged(false, null, ex.Message);
        }
    }

    /* Legacy UDP broadcast discovery retained only as historical reference; mDNS is the active discovery path.
    private UdpClient CreateDiscoverySocket(LanInterfaceInfo lanInterface, int port)
    {
        UdpClient? udp = null;
        string operation = "socket creation";
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            logger.LogInformation("Local PVP UDP discovery listener socket created.");

            operation = "socket configuration";
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            logger.LogInformation("Local PVP UDP discovery listener socket configured for broadcast.");

            operation = "socket bind";
            udp.Client.Bind(new IPEndPoint(lanInterface.Ipv4Address, port));
            logger.LogInformation(
                "Local PVP UDP discovery listener socket bound to {LocalEndpoint}.",
                udp.Client.LocalEndPoint);
            logger.LogInformation(
                "Local PVP UDP discovery broadcast endpoint is {BroadcastEndpoint}.",
                new IPEndPoint(lanInterface.BroadcastAddress, port));
            return udp;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Local PVP UDP discovery listener failed during {Operation}. LocalEndpoint: {LocalEndpoint}, DiscoveryPort: {DiscoveryPort}, Platform: {Platform}, Runtime: {Runtime}.",
                operation, udp?.Client.LocalEndPoint, port, GetPlatformDescription(), RuntimeInformation.FrameworkDescription);
            udp?.Dispose();
            throw;
        }
    }

    private async Task ListenForDiscoveryQueriesAsync(UdpClient udp, LanInterfaceInfo lanInterface, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Local PVP UDP discovery listener running on {UdpEndpoint}. BroadcastEndpoint: {BroadcastEndpoint}.",
            udp.Client.LocalEndPoint, new IPEndPoint(lanInterface.BroadcastAddress, DiscoveryPort));
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(cancellationToken);
                if (!IsDiscoveryQuery(result.Buffer))
                    continue;

                logger.LogInformation(
                    "Local PVP discovery query received from {Endpoint}.",
                    result.RemoteEndPoint);
                logger.LogDebug("Local PVP discovery query validated from {Endpoint}.", result.RemoteEndPoint);
                byte[] response = CreateRoomAdvertisement(lanInterface);
                await udp.SendAsync(response, result.RemoteEndPoint);
                logger.LogInformation(
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

    private byte[] CreateRoomAdvertisement(LanInterfaceInfo lanInterface) =>
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Protocol,
            Version = ProtocolVersion,
            Type = "Advertisement",
            RoomId,
            HostName = LocalPlayerName,
            HostIp = lanInterface.Ipv4Address.ToString(),
            TcpPort = GamePort
        }, JsonOptions));

    private async Task DiscoverRoomsAsync(LanInterfaceInfo lanInterface, CancellationTokenSource discoveryLifetime)
    {
        CancellationToken cancellationToken = discoveryLifetime.Token;
        UdpClient? udp = null;
        IPEndPoint? localEndpoint = null;
        string operation = "socket creation";

        logger.LogInformation(
            "Local PVP discovery starting. DiscoveryPort: {DiscoveryPort}, Platform: {Platform}, Runtime: {Runtime}.",
            DiscoveryPort, GetPlatformDescription(), RuntimeInformation.FrameworkDescription);
        LogLanInterface(lanInterface);

        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            logger.LogInformation("Local PVP discovery socket created.");

            operation = "socket configuration";
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            logger.LogInformation("Local PVP discovery socket configured for IPv4 broadcast.");

            operation = "socket bind";
            udp.Client.Bind(new IPEndPoint(lanInterface.Ipv4Address, 0));
            localEndpoint = udp.Client.LocalEndPoint as IPEndPoint;
            logger.LogInformation("Local PVP discovery socket bound to {LocalEndpoint}.", localEndpoint);

            byte[] query = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                Protocol,
                Version = ProtocolVersion,
                Type = "Query"
            }, JsonOptions));
            IPEndPoint broadcastEndpoint = new(lanInterface.BroadcastAddress, DiscoveryPort);
            logger.LogDebug("Local PVP discovery query prepared for {BroadcastEndpoint}.", broadcastEndpoint);

            while (!cancellationToken.IsCancellationRequested)
            {
                operation = "UDP query send";
                try
                {
                    logger.LogDebug(
                        "Local PVP discovery query sending. LocalEndpoint: {LocalEndpoint}, BroadcastEndpoint: {BroadcastEndpoint}, DiscoveryPort: {DiscoveryPort}.",
                        localEndpoint, broadcastEndpoint, DiscoveryPort);
                    int bytesSent = await udp.SendAsync(query, broadcastEndpoint);
                    logger.LogDebug(
                        "Local PVP discovery query sent. LocalEndpoint: {LocalEndpoint}, BroadcastEndpoint: {BroadcastEndpoint}, BytesSent: {BytesSent}.",
                        localEndpoint, broadcastEndpoint, bytesSent);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    logger.LogError(
                        ex,
                        "Local PVP discovery query send failed. LocalEndpoint: {LocalEndpoint}, BroadcastEndpoint: {BroadcastEndpoint}, DiscoveryPort: {DiscoveryPort}.",
                        localEndpoint, broadcastEndpoint, DiscoveryPort);
                    throw;
                }

                using CancellationTokenSource receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveTimeout.CancelAfter(500);
                try
                {
                    logger.LogDebug("Local PVP discovery waiting for responses on {LocalEndpoint}.", localEndpoint);
                    operation = "UDP response receive";
                    while (!receiveTimeout.IsCancellationRequested)
                    {
                        UdpReceiveResult result = await udp.ReceiveAsync(receiveTimeout.Token);
                        logger.LogDebug("Local PVP discovery response received from {RemoteEndpoint}.", result.RemoteEndPoint);
                        await ProcessAdvertisementAsync(result, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    logger.LogTrace("Local PVP discovery response window elapsed.");
                }

                RemoveExpiredRooms();
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Local PVP discovery canceled.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Local PVP discovery failed during {Operation}. LocalEndpoint: {LocalEndpoint}, DiscoveryPort: {DiscoveryPort}, Platform: {Platform}, Runtime: {Runtime}.",
                operation, localEndpoint, DiscoveryPort, GetPlatformDescription(), RuntimeInformation.FrameworkDescription);
            RaiseConnectionChanged(false, null, ex.Message);
        }
        finally
        {
            udp?.Dispose();
            logger.LogInformation("Local PVP discovery socket closed.");
            if (ReferenceEquals(discoveryCancellation, discoveryLifetime))
            {
                discoveryCancellation = null;
                discoveryTask = null;
                discoveryLifetime.Dispose();
            }
        }
    }

    private async Task AdvertiseRoomAsync(LanInterfaceInfo lanInterface, CancellationToken cancellationToken)
    {
        UdpClient? udp = null;
        string operation = "socket creation";
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            logger.LogDebug("Local PVP room advertisement socket created.");
            operation = "socket configuration";
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(lanInterface.Ipv4Address, 0));
            logger.LogDebug("Local PVP room advertisement socket configured for broadcast.");

            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] advertisement = CreateRoomAdvertisement(lanInterface);
                IPEndPoint broadcastEndpoint = new(lanInterface.BroadcastAddress, DiscoveryPort);
                logger.LogDebug("Local PVP sending room advertisement for {RoomId} to {BroadcastEndpoint} from {LocalEndpoint}.", RoomId, broadcastEndpoint, udp.Client.LocalEndPoint);
                operation = "UDP advertisement send";
                int bytesSent = await udp.SendAsync(advertisement, broadcastEndpoint);
                logger.LogDebug(
                    "Local PVP room advertisement sent. RoomId: {RoomId}, LocalEndpoint: {LocalEndpoint}, BroadcastEndpoint: {BroadcastEndpoint}, BytesSent: {BytesSent}.",
                    RoomId, udp.Client.LocalEndPoint, broadcastEndpoint, bytesSent);
                await Task.Delay(AdvertisementInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Local PVP room advertisement failed during {Operation}. DiscoveryPort: {DiscoveryPort}, Platform: {Platform}, Runtime: {Runtime}.",
                operation, DiscoveryPort, GetPlatformDescription(), RuntimeInformation.FrameworkDescription);
        }
        finally
        {
            udp?.Dispose();
            logger.LogDebug("Local PVP room advertisement socket closed.");
        }
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
            string? advertisedHostIp = root.TryGetProperty("HostIp", out JsonElement hostIpElement) && hostIpElement.ValueKind == JsonValueKind.String
                ? hostIpElement.GetString()
                : null;
            string hostAddress = result.RemoteEndPoint.Address.ToString();
            LocalPvpRoom room = new(roomId, hostName, hostAddress, tcpPort, DateTime.UtcNow);
            logger.LogDebug(
                "Local PVP discovery advertisement validated. RemoteEndpoint: {RemoteEndpoint}, AdvertisedHostIp: {AdvertisedHostIp}, SelectedHostAddress: {SelectedHostAddress}, RoomId: {RoomId}, TcpPort: {TcpPort}.",
                result.RemoteEndPoint, advertisedHostIp, hostAddress, roomId, tcpPort);
            bool changed;
            lock (stateLock)
            {
                changed = !rooms.TryGetValue(roomId, out LocalPvpRoom? old) ||
                    old.HostAddress != room.HostAddress || old.HostName != room.HostName || old.TcpPort != room.TcpPort;
                rooms[roomId] = room;
            }
            if (changed)
            {
                IReadOnlyList<LocalPvpRoom> snapshot = DiscoveredRooms;
                logger.LogInformation(
                    "Local PVP room discovered. RoomId: {RoomId}, HostName: {HostName}, HostAddress: {HostAddress}, TcpPort: {TcpPort}, RoomCount: {RoomCount}.",
                    room.RoomId, room.HostName, room.HostAddress, room.TcpPort, snapshot.Count);
                logger.LogDebug("Local PVP RoomsChanged event raised. RoomCount: {RoomCount}.", snapshot.Count);
                RoomsChanged?.Invoke(this, new(snapshot));
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
            IReadOnlyList<LocalPvpRoom> snapshot = DiscoveredRooms;
            logger.LogInformation("Local PVP expired rooms removed. RoomCount: {RoomCount}.", snapshot.Count);
            logger.LogDebug("Local PVP RoomsChanged event raised after expiration. RoomCount: {RoomCount}.", snapshot.Count);
            RoomsChanged?.Invoke(this, new(snapshot));
        }
    }

    */

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
            logger.LogInformation("Local PVP Hello received. RemotePlayerName: {RemotePlayerName}.", playerName);
            if (!IsHost || string.Equals(playerName, LocalPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Local PVP handshake rejected for player {PlayerName}.", playerName);
                await SendWireMessageAsync(LocalPvpMessageType.HelloRejected, new { Reason = "Nome de usuário inválido ou já utilizado." }, cancellationToken);
                return;
            }

            RemotePlayerName = playerName.Trim();
            logger.LogInformation("Local PVP handshake accepted for remote player {RemotePlayerName}.", RemotePlayerName);
            await SendWireMessageAsync(LocalPvpMessageType.HelloAccepted, new { PlayerName = LocalPlayerName }, cancellationToken);
            logger.LogInformation("Local PVP HelloAccepted sent.");
            if (peerConnected?.TrySetResult(true) == true)
                logger.LogInformation("Local PVP peerConnected completed.");
            RaiseConnectionChanged(true, RemotePlayerName);
            return;
        }

        if (message.Type == LocalPvpMessageType.HelloAccepted)
        {
            RemotePlayerName = message.Payload.GetProperty("PlayerName").GetString();
            logger.LogInformation("Local PVP HelloAccepted received. RemotePlayerName: {RemotePlayerName}.", RemotePlayerName);
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
        mdns?.Stop();
        discoveryCancellation?.Dispose();
        discoveryCancellation = null;
        mdns = null;
        lock (stateLock)
            rooms.Clear();
        await Task.CompletedTask;
    }

    private void OnMdnsRoomDiscovered(LocalPvpRoom room)
    {
        bool changed;
        lock (stateLock)
        {
            changed = !rooms.TryGetValue(room.RoomId, out LocalPvpRoom? old) ||
                old.HostAddress != room.HostAddress || old.HostName != room.HostName || old.TcpPort != room.TcpPort;
            rooms[room.RoomId] = room;
        }
        if (changed)
        {
            IReadOnlyList<LocalPvpRoom> snapshot = DiscoveredRooms;
            logger.LogInformation("Local PVP room discovered through mDNS. RoomId: {RoomId}, HostName: {HostName}, HostAddress: {HostAddress}, TcpPort: {TcpPort}.", room.RoomId, room.HostName, room.HostAddress, room.TcpPort);
            RoomsChanged?.Invoke(this, new(snapshot));
        }
    }

    private void OnMdnsRoomRemoved(string roomId)
    {
        lock (stateLock)
            rooms.Remove(roomId);
        RoomsChanged?.Invoke(this, new(DiscoveredRooms));
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

    private LanInterfaceInfo SelectLanInterface()
    {
        try
        {
            List<LanInterfaceInfo> candidates = [];
            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
                    continue;

                foreach (UnicastIPAddressInformation address in network.GetIPProperties().UnicastAddresses)
                {
                    IPAddress ipv4Address = address.Address;
                    IPAddress? subnetMask = address.IPv4Mask;
                    if (ipv4Address.AddressFamily != AddressFamily.InterNetwork ||
                        subnetMask is null ||
                        IPAddress.IsLoopback(ipv4Address) ||
                        IsLinkLocal(ipv4Address))
                        continue;

                    IPAddress broadcastAddress = CalculateBroadcastAddress(ipv4Address, subnetMask);
                    candidates.Add(new LanInterfaceInfo(
                        network.Name,
                        network.Description,
                        network.NetworkInterfaceType,
                        ipv4Address,
                        subnetMask,
                        broadcastAddress));
                }
            }

            LanInterfaceInfo? selected = candidates
                .OrderBy(GetInterfacePriority)
                .FirstOrDefault();
            if (selected is null)
                throw new InvalidOperationException("Nenhuma interface IPv4 LAN operacional foi encontrada.");

            return selected;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP LAN interface selection failed.");
            throw;
        }
    }

    private void LogLanInterface(LanInterfaceInfo lanInterface) =>
        logger.LogInformation(
            "Local PVP LAN interface selected. Interface: {InterfaceName}, InterfaceType: {InterfaceType}, IPv4Address: {IPv4Address}, SubnetMask: {SubnetMask}, BroadcastAddress: {BroadcastAddress}.",
            lanInterface.Name, lanInterface.InterfaceType, lanInterface.Ipv4Address, lanInterface.SubnetMask, lanInterface.BroadcastAddress);

    private static int GetInterfacePriority(LanInterfaceInfo lanInterface)
    {
        int priority = lanInterface.InterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 ? 0 : 10;
        if (!IsPrivateAddress(lanInterface.Ipv4Address))
            priority += 5;

        string description = $"{lanInterface.Name} {lanInterface.Description}";
        if (description.Contains("vpn", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("vmware", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("emulator", StringComparison.OrdinalIgnoreCase))
            priority += 20;

        return priority;
    }

    private static IPAddress CalculateBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte[] maskBytes = subnetMask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            throw new InvalidOperationException("A interface LAN precisa ter endereço IPv4 e máscara IPv4.");

        byte[] broadcastBytes = new byte[4];
        for (int index = 0; index < broadcastBytes.Length; index++)
            broadcastBytes[index] = (byte)(addressBytes[index] | ~maskBytes[index]);

        return new IPAddress(broadcastBytes);
    }

    private static bool IsLinkLocal(IPAddress address) =>
        address.GetAddressBytes() is [169, 254, _, _];

    private static bool IsPrivateAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
            (bytes[0] == 10 ||
             (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
             (bytes[0] == 192 && bytes[1] == 168));
    }

    private static string GetPlatformDescription() =>
        OperatingSystem.IsAndroid() ? "Android" :
        OperatingSystem.IsIOS() ? "iOS" :
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsMacCatalyst() ? "MacCatalyst" :
        Environment.OSVersion.Platform.ToString();

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

    private sealed record LanInterfaceInfo(
        string Name,
        string Description,
        NetworkInterfaceType InterfaceType,
        IPAddress Ipv4Address,
        IPAddress SubnetMask,
        IPAddress BroadcastAddress);
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
