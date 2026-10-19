using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LiteNetLib;
using LiteNetLib.Utils;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Game.Entities;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Core;
using MemoAna.Game.Dtos;
using MemoAna.Game.Enums;
using MemoAna.Game.Models;
using Microsoft.Extensions.Logging;

namespace MemoAna.Game.Services;

public sealed class PVPService : IPVPService
{
    private const string ProtocolVersion = "memoana-pvp-1";
    private const int DefaultPort = 35678;
    private const int DefaultDiscoveryPort = 35679;
    private readonly IThemeService themeService;
    private readonly IRepository<GameStatisticsEntity>? statisticsRepository;
    private readonly ILogger<PVPService> logger;
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private CancellationTokenSource operationCts = new();
    private readonly Dictionary<string, PVPHostInfo> discoveredHosts = new(StringComparer.Ordinal);
    private readonly ObservableCollection<KeyValuePair<int, MemoryCard>> cards = [];
    private NetManager? gameManager;
    private NetManager? discoveryManager;
    private EventBasedNetListener? gameListener;
    private EventBasedNetListener? discoveryListener;
    private NetPeer? peer;
    private CancellationTokenSource? networkLoopCts;
    private Task? networkLoopTask;
    private PVPGameSettings? settings;
    private PVPPlayerRole localRole;
    private Guid sessionId;
    private PVPPlayerRole currentTurn;
    private int hostScore;
    private int clientScore;
    private string hostPlayerName = "Host";
    private string clientPlayerName = "Client";
    private int hostHits;
    private int hostMisses;
    private int hostMoves;
    private int clientHits;
    private int clientMisses;
    private int clientMoves;
    private int hostStreak;
    private int clientStreak;
    private Guid? finishedSessionId;
    private int firstPosition = -1;
    private int secondPosition = -1;
    private bool mismatchPending;
    private PVPPlayerRole currentMovePlayer;

    public PVPService(
        IThemeService themeService,
        ILogger<PVPService> logger,
        IRepository<GameStatisticsEntity>? statisticsRepository = null)
    {
        this.themeService = themeService;
        this.logger = logger;
        this.statisticsRepository = statisticsRepository;
    }

    public IReadOnlyList<KeyValuePair<int, MemoryCard>> CurrentCards => cards;
    public Guid? SessionId => sessionId == Guid.Empty ? null : sessionId;
    public PVPPlayerRole LocalRole => localRole;
    public PVPPlayerRole CurrentTurn => currentTurn;
    public bool IsHost => localRole == PVPPlayerRole.Host;
    public bool IsConnected => peer?.ConnectionState == ConnectionState.Connected;
    public bool IsGameActive { get; private set; }
    public bool IsLocalTurn => IsGameActive && CurrentTurn == LocalRole;
    public string LocalPlayerName => IsHost ? hostPlayerName : clientPlayerName;
    public string RemotePlayerName => IsHost ? clientPlayerName : hostPlayerName;
    public int LocalScore => IsHost ? hostScore : clientScore;
    public int RemoteScore => IsHost ? clientScore : hostScore;
    public PVPPlayerStatistics LocalStatistics => GetStatistics(LocalRole);
    public PVPPlayerStatistics RemoteStatistics => GetStatistics(IsHost ? PVPPlayerRole.Client : PVPPlayerRole.Host);

    public event EventHandler? HostStarted;
    public event EventHandler? Connected;
    public event EventHandler<PVPHostDiscoveredEventArgs>? HostDiscovered;
    public event EventHandler<PVPStateChangedEventArgs>? SessionStarted;
    public event EventHandler<PVPCardEventArgs>? CardChanged;
    public event EventHandler<PVPStateChangedEventArgs>? TurnChanged;
    public event EventHandler<PVPGameFinishedEventArgs>? GameFinished;
    public event EventHandler? PlayerDisconnected;
    public event EventHandler<PVPErrorEventArgs>? Error;

    public async Task StartHostAsync(PVPGameSettings hostSettings, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostSettings.ThemeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostSettings.PlayerName);
        await StopAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        settings = hostSettings with
        {
            Port = hostSettings.Port == 0 ? DefaultPort : hostSettings.Port,
            DiscoveryPort = hostSettings.DiscoveryPort == 0 ? DefaultDiscoveryPort : hostSettings.DiscoveryPort
        };
        localRole = PVPPlayerRole.Host;
        hostPlayerName = hostSettings.PlayerName.Trim();
        clientPlayerName = "Client";
        ResetStatistics();
        sessionId = Guid.NewGuid();
        await CreateBoardAsync(settings, cancellationToken);
        StartGameManager(settings.Port);
        StartDiscoveryManager(settings.DiscoveryPort);
        logger.LogInformation(
            "PVP host '{PlayerName}' started. GamePort={GamePort}, DiscoveryPort={DiscoveryPort}, SessionId={SessionId}, ProtocolVersion={ProtocolVersion}",
            settings.PlayerName, settings.Port, settings.DiscoveryPort, sessionId, ProtocolVersion);
        HostStarted?.Invoke(this, System.EventArgs.Empty);
    }

    public async Task<IReadOnlyList<PVPHostInfo>> DiscoverHostsAsync(
        int discoveryPort = DefaultDiscoveryPort,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        lock (discoveredHosts)
            discoveredHosts.Clear();
        var result = new List<PVPHostInfo>();
        var clientDiscoveryListener = new EventBasedNetListener();
        var clientDiscoveryManager = new NetManager(clientDiscoveryListener)
        {
            UnconnectedMessagesEnabled = true
        };
        clientDiscoveryListener.NetworkReceiveUnconnectedEvent += (endpoint, reader, _) =>
        {
            if (reader.AvailableBytes <= 0)
                return;
            logger.LogInformation("Discover Host starting");
            try
            {
                PVPWireMessage? message = JsonSerializer.Deserialize<PVPWireMessage>(reader.GetString());
                if (message?.Type != PVPMessageType.DiscoveryResponse)
                {
                    logger.LogWarning("Ignoring discovery packet of type {MessageType} from {Endpoint}", message?.Type, endpoint);
                    return;
                }

                if (message.ProtocolVersion != ProtocolVersion)
                {
                    logger.LogWarning("Ignoring discovery response with protocol {ProtocolVersion} from {Endpoint}", message.ProtocolVersion, endpoint);
                    return;
                }

                if (message.Host is null)
                {
                    logger.LogWarning("Ignoring discovery response without host information from {Endpoint}", endpoint);
                    return;
                }

                string hostKey = $"{message.Host.HostName}:{message.Host.Port}:{message.Host.DiscoveryPort}:{message.SessionId}";
                bool firstResponse;
                lock (discoveredHosts)
                {
                    firstResponse = !discoveredHosts.ContainsKey(hostKey);
                    if (firstResponse)
                        discoveredHosts[hostKey] = message.Host;
                }
                if (firstResponse)
                    HostDiscovered?.Invoke(this, new(message.Host));
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Ignoring malformed PVP discovery response from {Endpoint}", endpoint);
            }
        };

        if (!clientDiscoveryManager.Start(0))
            throw new InvalidOperationException("Could not start the PVP discovery socket.");

        var request = new PVPWireMessage(ProtocolVersion, PVPMessageType.DiscoveryRequest, null, null, null);
        SendBroadcast(clientDiscoveryManager, request, discoveryPort);
        foreach (IPAddress broadcastAddress in GetDirectedBroadcastAddresses())
            SendUnconnected(clientDiscoveryManager, request, new IPEndPoint(broadcastAddress, discoveryPort));
        // LiteNetLib's broadcast socket does not loop a broadcast back into a
        // second manager in the same process. This extra endpoint is only for
        // the in-process protocol test; LAN discovery uses the broadcasts above.
        SendUnconnected(clientDiscoveryManager, request, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        logger.LogInformation("PVP discovery started. DiscoveryPort={DiscoveryPort}", discoveryPort);

        try
        {
            DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                clientDiscoveryManager.PollEvents();
                await Task.Delay(50, cancellationToken);
            }
        }
        finally
        {
            clientDiscoveryManager.Stop();
        }

        lock (discoveredHosts)
            result.AddRange(discoveredHosts.Values);
        return result;
    }

    public async Task ConnectAsync(PVPHostInfo host, string playerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host.Address);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        await StopAsync(cancellationToken);
        settings = new PVPGameSettings(host.Difficulty, host.ThemeName, playerName, host.Port, host.DiscoveryPort);
        localRole = PVPPlayerRole.Client;
        clientPlayerName = playerName.Trim();
        hostPlayerName = string.IsNullOrWhiteSpace(host.HostName) ? "Host" : host.HostName;
        ResetStatistics();
        sessionId = Guid.Empty;
        StartGameManager(0);
        StartNetworkLoop();
        logger.LogInformation("PVP client connecting to {Address}:{Port}", host.Address, host.Port);
        try
        {
            if (gameManager!.Connect(host.Address, host.Port, ProtocolVersion) is null)
                throw new InvalidOperationException($"Could not connect to PVP host {host.Address}:{host.Port}.");

            await WaitForConnectionAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RaiseError($"Could not connect to PVP host {host.Address}:{host.Port}.", exception);
            await StopAsync();
            throw;
        }
    }

    public async Task StartGameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsHost || !IsConnected)
            throw new InvalidOperationException("Only a connected host can start a PVP game.");

        await stateGate.WaitAsync(cancellationToken);
        try
        {
            IsGameActive = true;
            currentTurn = PVPPlayerRole.Host;
            Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.GameStart, sessionId,
                new PVPGameState(sessionId, settings!.ThemeName, settings.Difficulty, ToCardStates(),
                    currentTurn, hostScore, clientScore), null));
            PublishState(SessionStarted);
            logger.LogInformation("PVP session started: {SessionId}", sessionId);
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async Task FlipCardAsync(int position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await stateGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsGameActive || !IsLocalTurn || mismatchPending ||
                firstPosition >= 0 && secondPosition >= 0)
                return;
            if (cards.All(card => card.Key != position) ||
                cards.First(card => card.Key == position).Value.IsMatched ||
                cards.First(card => card.Key == position).Value.IsFaceUp)
                return;

            if (IsHost)
                await ApplyAuthoritativeFlipAsync(position, PVPPlayerRole.Host);
            else
                Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.CardFlipRequest, sessionId, null,
                    new PVPCardMove(position, CurrentTurn)));
        }
        finally
        {
            stateGate.Release();
        }
    }

    public async Task LeaveGameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        operationCts.Cancel();
        operationCts.Dispose();
        operationCts = new CancellationTokenSource();
        networkLoopCts?.Cancel();
        if (networkLoopTask is not null)
        {
            try
            {
                await networkLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        networkLoopCts?.Dispose();
        networkLoopCts = null;
        networkLoopTask = null;
        peer?.Disconnect();
        gameManager?.Stop();
        discoveryManager?.Stop();
        gameManager = null;
        discoveryManager = null;
        gameListener = null;
        discoveryListener = null;
        peer = null;
        IsGameActive = false;
        sessionId = Guid.Empty;
        cards.Clear();
        hostScore = 0;
        clientScore = 0;
        hostPlayerName = "Host";
        clientPlayerName = "Client";
        ResetStatistics();
        finishedSessionId = null;
        currentTurn = default;
        firstPosition = -1;
        secondPosition = -1;
        mismatchPending = false;
        currentMovePlayer = default;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        operationCts.Dispose();
        stateGate.Dispose();
    }

    private void StartGameManager(int port)
    {
        gameListener = new EventBasedNetListener();
        gameListener.PeerConnectedEvent += OnPeerConnected;
        gameListener.PeerDisconnectedEvent += OnPeerDisconnected;
        gameListener.NetworkReceiveEvent += OnNetworkReceive;
        gameListener.ConnectionRequestEvent += OnConnectionRequest;
        gameListener.NetworkErrorEvent += (_, error) =>
            RaiseError("PVP game network error.", new SocketException((int)error));
        gameManager = new NetManager(gameListener) { AutoRecycle = true, UnconnectedMessagesEnabled = false };
        if (!gameManager.Start(port))
            throw new InvalidOperationException($"Could not start the PVP socket on port {port}.");

    }

    private void StartDiscoveryManager(int port)
    {
        discoveryListener = new EventBasedNetListener();
        discoveryListener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;
        discoveryListener.NetworkErrorEvent += (_, error) =>
            RaiseError("PVP discovery network error.", new SocketException((int)error));
        discoveryManager = new NetManager(discoveryListener) { UnconnectedMessagesEnabled = true };
        if (!discoveryManager.Start(port))
        {
            gameManager?.Stop();
            gameManager = null;
            throw new InvalidOperationException($"Could not start the PVP discovery socket on port {port}.");
        }

        StartNetworkLoop();
    }

    private void StartNetworkLoop()
    {
        // The loop is started after the active managers exist so it can poll all of them.
        if (networkLoopTask is not null)
            return;

        networkLoopCts = new CancellationTokenSource();
        networkLoopTask = RunNetworkLoopAsync(networkLoopCts.Token);
    }

    private async Task RunNetworkLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                gameManager?.PollEvents();
                discoveryManager?.PollEvents();
                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        if (!IsHost || peer is not null)
        {
            request.Reject();
            return;
        }
        request.AcceptIfKey(ProtocolVersion);
    }

    private void OnPeerConnected(NetPeer connectedPeer)
    {
        peer = connectedPeer;
        logger.LogInformation("PVP connection accepted from {Address}:{Port}", connectedPeer.Address, connectedPeer.Port);
        Connected?.Invoke(this, System.EventArgs.Empty);
        if (IsHost)
        {
            Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.ConnectionAccepted, sessionId, null,
                new PVPPlayerInfo(settings!.PlayerName, PVPPlayerRole.Host)));
        }
        else
        {
            Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.ConnectionHello, null, null,
                new PVPPlayerInfo(settings!.PlayerName, PVPPlayerRole.Client)));
        }
    }

    private void OnPeerDisconnected(NetPeer disconnectedPeer, DisconnectInfo _)
    {
        if (peer == disconnectedPeer)
            peer = null;
        IsGameActive = false;
        if (!IsHost)
        {
            sessionId = Guid.Empty;
            cards.Clear();
            hostScore = 0;
            clientScore = 0;
            currentTurn = default;
            firstPosition = -1;
            secondPosition = -1;
            mismatchPending = false;
        }
        logger.LogInformation("PVP player disconnected from {Address}:{Port}", disconnectedPeer.Address, disconnectedPeer.Port);
        PlayerDisconnected?.Invoke(this, System.EventArgs.Empty);
    }

    private async void OnNetworkReceive(NetPeer sourcePeer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (sourcePeer != peer)
            return;

        try
        {
            string payload = reader.GetString();
            PVPWireMessage? message = JsonSerializer.Deserialize<PVPWireMessage>(payload);
            if (message is null)
                throw new InvalidOperationException("The received PVP message was empty.");
            if (message.ProtocolVersion != ProtocolVersion)
                throw new InvalidOperationException($"Unsupported PVP protocol version '{message.ProtocolVersion}'.");
            await HandleMessageAsync(message);
        }
        catch (JsonException exception)
        {
            RaiseError("Invalid PVP message received.", exception);
        }
        catch (Exception exception)
        {
            RaiseError("Could not process PVP message.", exception);
        }
    }

    private void OnNetworkReceiveUnconnected(IPEndPoint endpoint, NetPacketReader reader, UnconnectedMessageType _)
    {
        try
        {
            PVPWireMessage? message = JsonSerializer.Deserialize<PVPWireMessage>(reader.GetString());
            if (message?.Type != PVPMessageType.DiscoveryRequest || !IsHost || settings is null)
                return;

            logger.LogInformation("DiscoveryRequest received from {Address}:{Port}; DiscoveryPort={DiscoveryPort}",
                endpoint.Address, endpoint.Port, settings.DiscoveryPort);
            string advertisedAddress = GetAdvertisedAddress(endpoint);
            var host = new PVPHostInfo(settings.PlayerName, advertisedAddress, settings.Port,
                settings.DiscoveryPort, settings.Difficulty, settings.ThemeName, peer is null ? 1 : 2, 2, ProtocolVersion);
            var response = new PVPWireMessage(ProtocolVersion, PVPMessageType.DiscoveryResponse, sessionId, null, host);
            SendUnconnected(discoveryManager!, response, endpoint);
            logger.LogInformation(
                "DiscoveryResponse sent to {Address}:{Port}; DiscoveryPort={DiscoveryPort}, AdvertisingHost={AdvertisedAddress}:{GamePort}, SessionId={SessionId}, ProtocolVersion={ProtocolVersion}",
                endpoint.Address, endpoint.Port, settings.DiscoveryPort, advertisedAddress, settings.Port, sessionId, ProtocolVersion);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Ignoring malformed PVP discovery request from {Endpoint}", endpoint);
        }
    }

    private async Task HandleMessageAsync(PVPWireMessage message)
    {
        await stateGate.WaitAsync();
        try
        {
            if (message.SessionId is not null && message.SessionId != sessionId &&
                (IsGameActive || message.Type != PVPMessageType.ConnectionAccepted))
                return;
            switch (message.Type)
            {
                case PVPMessageType.ConnectionHello when IsHost:
                    if (message.Player?.Role != PVPPlayerRole.Client ||
                        string.IsNullOrWhiteSpace(message.Player.Name))
                        throw new InvalidOperationException("The PVP client handshake is invalid.");
                    clientPlayerName = message.Player.Name.Trim();
                    break;
                case PVPMessageType.ConnectionAccepted when !IsHost &&
                    message.SessionId is Guid acceptedSession &&
                    message.Player?.Role == PVPPlayerRole.Host:
                    sessionId = acceptedSession;
                    hostPlayerName = message.Player.Name.Trim();
                    break;
                case PVPMessageType.ConnectionAccepted when !IsHost:
                    throw new InvalidOperationException("The PVP host handshake is invalid.");
                case PVPMessageType.GameStart when !IsHost && message.GameState is not null:
                    if (message.GameState.SessionId != sessionId)
                        return;
                    await ApplyGameStateAsync(message.GameState);
                    PublishState(SessionStarted);
                    break;
                case PVPMessageType.CardFlipRequest when IsHost && message.CardMove is not null:
                    if (message.CardMove.Player == PVPPlayerRole.Client &&
                        currentTurn == PVPPlayerRole.Client)
                        await ApplyAuthoritativeFlipAsync(message.CardMove.Position, PVPPlayerRole.Client);
                    break;
                case PVPMessageType.CardFlipped when !IsHost && message.CardMove is not null:
                    ApplyRemoteFlip(message.CardMove.Position, message.CardMove.Player);
                    break;
                case PVPMessageType.TurnResolved when !IsHost && message.Resolution is not null:
                    ApplyResolution(message.Resolution);
                    break;
                case PVPMessageType.GameFinished when !IsHost && message.Result is not null &&
                    message.Result.SessionId == sessionId:
                    IsGameActive = false;
                    ApplyResultStatistics(message.Result);
                    GameFinished?.Invoke(this, new(message.Result));
                    break;
            }
        }
        finally
        {
            stateGate.Release();
        }
    }

    private async Task ApplyAuthoritativeFlipAsync(int position, PVPPlayerRole player)
    {
        KeyValuePair<int, MemoryCard>? entry = cards.FirstOrDefault(card => card.Key == position);
        if (entry is null || entry.Value.Value.IsMatched || entry.Value.Value.IsFaceUp)
            return;

        entry.Value.Value.IsFaceUp = true;
        if (firstPosition < 0)
        {
            firstPosition = position;
            currentMovePlayer = player;
        }
        else
            secondPosition = position;
        var move = new PVPCardMove(position, player);
        Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.CardFlipped, sessionId, null, move));
        CardChanged?.Invoke(this, new(position, player, false));
        if (secondPosition >= 0)
            await ResolveAuthoritativeTurnAsync(player);
    }

    private async Task ResolveAuthoritativeTurnAsync(PVPPlayerRole player)
    {
        MemoryCard first = cards.First(card => card.Key == firstPosition).Value;
        MemoryCard second = cards.First(card => card.Key == secondPosition).Value;
        bool matched = first.PairId == second.PairId;
        IncrementMove(player);
        if (matched)
        {
            first.IsMatched = true;
            second.IsMatched = true;
            IncrementHit(player);
        }
        else
        {
            mismatchPending = true;
            IncrementMiss(player);
        }

        CardChanged?.Invoke(this, new(firstPosition, player, matched));
        CardChanged?.Invoke(this, new(secondPosition, player, matched));
        int resolvedFirstPosition = firstPosition;
        int resolvedSecondPosition = secondPosition;
        currentTurn = matched ? player : player == PVPPlayerRole.Host ? PVPPlayerRole.Client : PVPPlayerRole.Host;
        var resolution = new PVPResolution(firstPosition, secondPosition, matched, currentTurn, hostScore, clientScore);
        Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.TurnResolved, sessionId, null, resolution));
        firstPosition = -1;
        secondPosition = -1;
        PublishState(TurnChanged);
        if (cards.All(card => card.Value.IsMatched))
        {
            IsGameActive = false;
            var result = new PVPGameResult(sessionId,
                hostScore == clientScore ? player : hostScore > clientScore ? PVPPlayerRole.Host : PVPPlayerRole.Client,
                hostScore, clientScore, settings?.ThemeName ?? string.Empty,
                settings?.Difficulty ?? 0, DateTime.UtcNow, [GetStatistics(PVPPlayerRole.Host), GetStatistics(PVPPlayerRole.Client)]);
            Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.GameFinished, sessionId, null, result));
            await PersistStatisticsAsync(result);
            GameFinished?.Invoke(this, new(result));
        }
        else if (!matched)
        {
            _ = HideMismatchAsync(resolvedFirstPosition, resolvedSecondPosition, sessionId);
        }
    }

    private void ApplyRemoteFlip(int position, PVPPlayerRole player)
    {
        if (!IsGameActive || mismatchPending || player != currentTurn ||
            cards.All(card => card.Key != position) ||
            cards.First(card => card.Key == position).Value.IsMatched ||
            cards.First(card => card.Key == position).Value.IsFaceUp ||
            secondPosition >= 0)
            return;
        cards.First(card => card.Key == position).Value.IsFaceUp = true;
        if (firstPosition < 0)
        {
            firstPosition = position;
            currentMovePlayer = player;
        }
        else
            secondPosition = position;
        CardChanged?.Invoke(this, new(position, player, false));
    }

    private void ApplyResolution(PVPResolution resolution)
    {
        if (!IsGameActive ||
            firstPosition != resolution.FirstPosition ||
            secondPosition != resolution.SecondPosition ||
            cards.All(card => card.Key != resolution.FirstPosition) ||
            cards.All(card => card.Key != resolution.SecondPosition))
            return;
        MemoryCard first = cards.First(card => card.Key == resolution.FirstPosition).Value;
        MemoryCard second = cards.First(card => card.Key == resolution.SecondPosition).Value;
        first.IsMatched = resolution.Matched;
        second.IsMatched = resolution.Matched;
        if (!resolution.Matched)
            mismatchPending = true;
        IncrementMove(currentMovePlayer);
        if (resolution.Matched)
            IncrementHit(currentMovePlayer);
        else
            IncrementMiss(currentMovePlayer);
        hostScore = resolution.HostScore;
        clientScore = resolution.ClientScore;
        currentTurn = resolution.NextTurn;
        firstPosition = -1;
        secondPosition = -1;
        currentMovePlayer = default;
        PublishState(TurnChanged);
        if (!resolution.Matched)
            _ = HideMismatchAsync(resolution.FirstPosition, resolution.SecondPosition, sessionId);
    }

    private async Task HideMismatchAsync(int firstCardPosition, int secondCardPosition, Guid expectedSessionId)
    {
        CancellationToken cancellationToken = operationCts.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            await stateGate.WaitAsync(cancellationToken);
            try
            {
                if (!IsGameActive || sessionId != expectedSessionId || !mismatchPending)
                    return;

                MemoryCard? first = cards.FirstOrDefault(card => card.Key == firstCardPosition).Value;
                MemoryCard? second = cards.FirstOrDefault(card => card.Key == secondCardPosition).Value;
                if (first is null || second is null)
                    return;

                first.IsFaceUp = false;
                second.IsFaceUp = false;
                mismatchPending = false;
                CardChanged?.Invoke(this, new(firstCardPosition, LocalRole, false));
                CardChanged?.Invoke(this, new(secondCardPosition, LocalRole, false));
            }
            finally
            {
                stateGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CreateBoardAsync(PVPGameSettings gameSettings, CancellationToken cancellationToken)
    {
        var theme = await themeService.GetThemeAsync(gameSettings.ThemeName)
            ?? throw new KeyNotFoundException($"Theme '{gameSettings.ThemeName}' was not found.");
        int pairCount = gameSettings.Difficulty switch { 1 => 10, 2 => 15, _ => 6 };
        var selected = theme.Base64Images.Where(image => !string.IsNullOrWhiteSpace(image))
            .Take(pairCount).Select((image, index) => (image, index)).ToList();
        var board = selected.SelectMany(item => new[] { item, item }).OrderBy(_ => Random.Shared.Next()).ToList();
        cards.Clear();
        for (int index = 0; index < board.Count; index++)
            cards.Add(new(index + 1, new MemoryCard { Id = index, PairId = board[index].index.ToString(), CardImage = board[index].image }));
    }

    private async Task ApplyGameStateAsync(PVPGameState state)
    {
        if (state.SessionId == Guid.Empty || state.Cards.Count == 0 ||
            state.Cards.Count % 2 != 0 ||
            state.Cards.Select(card => card.Position).Distinct().Count() != state.Cards.Count ||
            state.Cards.Any(card => card.Position <= 0 || card.ImageIndex < 0 ||
                string.IsNullOrWhiteSpace(card.PairId)))
            throw new InvalidOperationException("The host sent an invalid initial PVP board.");

        CardThemeDto theme = await themeService.GetThemeAsync(state.ThemeName)
            ?? throw new KeyNotFoundException($"Theme '{state.ThemeName}' was not found.");
        if (state.Cards.Any(card => card.ImageIndex >= theme.Base64Images.Count))
            throw new InvalidOperationException("The selected theme cannot render the PVP board.");
        sessionId = state.SessionId;
        cards.Clear();
        foreach (PVPCardState card in state.Cards)
        {
            string? image = card.ImageIndex >= 0 && card.ImageIndex < theme.Base64Images.Count
                ? theme.Base64Images.ElementAt(card.ImageIndex)
                : null;
            cards.Add(new(card.Position, new MemoryCard
            {
                Id = card.Position,
                PairId = card.PairId,
                CardImage = image,
                IsFaceUp = card.IsFaceUp,
                IsMatched = card.IsMatched
            }));
        }
        currentTurn = state.CurrentTurn;
        hostScore = state.HostScore;
        clientScore = state.ClientScore;
        IsGameActive = true;
    }

    private IReadOnlyList<PVPCardState> ToCardStates() =>
        cards.Select(card => new PVPCardState(
            card.Key,
            card.Value.PairId,
            int.TryParse(card.Value.PairId, out int imageIndex) ? imageIndex : card.Value.Id,
            card.Value.IsFaceUp,
            card.Value.IsMatched)).ToList();

    private void ResetStatistics()
    {
        hostScore = 0;
        clientScore = 0;
        hostHits = 0;
        hostMisses = 0;
        hostMoves = 0;
        clientHits = 0;
        clientMisses = 0;
        clientMoves = 0;
        hostStreak = 0;
        clientStreak = 0;
        finishedSessionId = null;
    }

    private void IncrementMove(PVPPlayerRole player)
    {
        if (player == PVPPlayerRole.Host)
            hostMoves++;
        else
            clientMoves++;
    }

    private void IncrementHit(PVPPlayerRole player)
    {
        if (player == PVPPlayerRole.Host)
        {
            hostHits++;
            hostStreak++;
            hostScore = (hostScore + 1) * hostStreak;
        }
        else
        {
            clientHits++;
            clientStreak++;
            clientScore = (clientScore + 1) * clientStreak;
        }
    }

    private void IncrementMiss(PVPPlayerRole player)
    {
        if (player == PVPPlayerRole.Host)
        {
            hostMisses++;
            hostStreak = 0;
        }
        else
        {
            clientMisses++;
            clientStreak = 0;
        }
    }

    private PVPPlayerStatistics GetStatistics(PVPPlayerRole role) => role == PVPPlayerRole.Host
        ? new(role, hostPlayerName, hostScore, hostHits, hostMisses, hostMoves)
        : new(role, clientPlayerName, clientScore, clientHits, clientMisses, clientMoves);

    private void ApplyResultStatistics(PVPGameResult result)
    {
        if (result.Statistics is null)
            return;

        foreach (PVPPlayerStatistics statistics in result.Statistics)
        {
            if (statistics.Role == PVPPlayerRole.Host)
            {
                hostPlayerName = statistics.Name;
                hostScore = statistics.Score;
                hostHits = statistics.Hits;
                hostMisses = statistics.Misses;
                hostMoves = statistics.TotalMoves;
            }
            else
            {
                clientPlayerName = statistics.Name;
                clientScore = statistics.Score;
                clientHits = statistics.Hits;
                clientMisses = statistics.Misses;
                clientMoves = statistics.TotalMoves;
            }
        }
    }

    private async Task PersistStatisticsAsync(PVPGameResult result)
    {
        if (statisticsRepository is null || finishedSessionId == result.SessionId || result.Statistics is null)
            return;

        finishedSessionId = result.SessionId;
        foreach (PVPPlayerStatistics player in result.Statistics)
        {
            await statisticsRepository.AddAsync(new GameStatisticsEntity
            {
                ThemeName = result.ThemeName,
                Difficulty = (GameDifficulty)result.Difficulty,
                GameMode = GameMode.PVP,
                PlayerName = player.Name,
                IsVictory = player.Role == result.Winner,
                TotalMoves = player.TotalMoves,
                SuccessfulMoves = player.Hits,
                Mistakes = player.Misses,
                FinalScore = player.FinalScore,
                PlayedAt = result.PlayedAt == default ? DateTime.UtcNow : result.PlayedAt
            }, CancellationToken.None);
        }
    }

    private void Send(PVPWireMessage message)
    {
        if (peer is null)
            return;
        var writer = new NetDataWriter();
        writer.Put(JsonSerializer.Serialize(message));
        peer.Send(writer, 0, DeliveryMethod.ReliableOrdered);
    }

    private static void SendBroadcast(NetManager networkManager, PVPWireMessage message, int port)
    {
        var writer = new NetDataWriter();
        writer.Put(JsonSerializer.Serialize(message));
        networkManager.SendBroadcast(writer, port);
    }

    private static void SendUnconnected(NetManager networkManager, PVPWireMessage message, IPEndPoint endpoint)
    {
        var writer = new NetDataWriter();
        writer.Put(JsonSerializer.Serialize(message));
        networkManager.SendUnconnectedMessage(writer, endpoint);
    }

    private static IEnumerable<IPAddress> GetDirectedBroadcastAddresses()
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                                    item.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                    address.IPv4Mask is null ||
                    IPAddress.IsLoopback(address.Address))
                    continue;

                byte[] ip = address.Address.GetAddressBytes();
                byte[] mask = address.IPv4Mask.GetAddressBytes();
                byte[] broadcast = new byte[4];
                for (int index = 0; index < broadcast.Length; index++)
                    broadcast[index] = (byte)(ip[index] | ~mask[index]);

                IPAddress endpoint = new(broadcast);
                if (!endpoint.Equals(IPAddress.Broadcast))
                    yield return endpoint;
            }
        }
    }

    private async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!IsConnected && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken);
        }
        if (!IsConnected)
            throw new TimeoutException("PVP connection timed out.");
    }

    private void PublishState(EventHandler<PVPStateChangedEventArgs>? handler)
    {
        var state = new PVPGameState(sessionId, settings?.ThemeName ?? string.Empty, settings?.Difficulty ?? 0,
            ToCardStates(), currentTurn, hostScore, clientScore);
        handler?.Invoke(this, new(state));
    }

    private void RaiseError(string message, Exception? exception = null)
    {
        logger.LogError(exception, "{Message}", message);
        Error?.Invoke(this, new(message, exception));
    }

    private static string GetAdvertisedAddress(IPEndPoint endpoint)
    {
        if (endpoint.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(endpoint.Address))
        {
            try
            {
                using var routeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                routeSocket.Connect(endpoint.Address, endpoint.Port);
                if (routeSocket.LocalEndPoint is IPEndPoint localEndpoint &&
                    !IPAddress.IsLoopback(localEndpoint.Address))
                    return localEndpoint.Address.ToString();
            }
            catch (SocketException)
            {
                // Fall back to deterministic interface selection below.
            }
        }

        string? fallback = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up &&
                                       networkInterface.NetworkInterfaceType is NetworkInterfaceType.Ethernet or
                                       NetworkInterfaceType.Wireless80211)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses
                .Where(address => IsUsableLanAddress(address.Address))
                .Select(address => (Priority: networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1,
                                    Address: address.Address)))
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Address.ToString(), StringComparer.Ordinal)
            .Select(item => item.Address.ToString())
            .FirstOrDefault()
            ?? NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
                .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(IsUsableLanAddress)
                .OrderBy(address => address.ToString(), StringComparer.Ordinal)
                .Select(address => address.ToString())
                .FirstOrDefault();

        return fallback ?? endpoint.Address.ToString();
    }

    private static bool IsUsableLanAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] != 169 || bytes[1] != 254;
    }

    private sealed record PVPWireMessage(
        string ProtocolVersion,
        PVPMessageType Type,
        Guid? SessionId,
        PVPGameState? GameState,
        object? Payload)
    {
        public PVPHostInfo? Host => Deserialize<PVPHostInfo>();
        public PVPPlayerInfo? Player => Deserialize<PVPPlayerInfo>();
        public PVPCardMove? CardMove => Deserialize<PVPCardMove>();
        public PVPResolution? Resolution => Deserialize<PVPResolution>();
        public PVPGameResult? Result => Deserialize<PVPGameResult>();

        private T? Deserialize<T>() where T : class =>
            Payload is JsonElement element
                ? element.Deserialize<T>()
                : Payload as T;
    }

    private sealed record PVPPlayerInfo(string Name, PVPPlayerRole Role);
    private sealed record PVPCardMove(int Position, PVPPlayerRole Player);
    private sealed record PVPResolution(int FirstPosition, int SecondPosition, bool Matched,
        PVPPlayerRole NextTurn, int HostScore, int ClientScore);
}
