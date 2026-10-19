using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using LiteNetLib;
using LiteNetLib.Utils;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Core;
using MemoAna.Game.Dtos;
using MemoAna.Game.Models;
using Microsoft.Extensions.Logging;

namespace MemoAna.Game.Services;

public sealed class PVPService : IPVPService
{
    private const string ProtocolVersion = "memoana-pvp-1";
    private const int DefaultPort = 35678;
    private const int DefaultDiscoveryPort = 35679;
    private readonly IThemeService themeService;
    private readonly ILogger<PVPService> logger;
    private readonly SemaphoreSlim stateGate = new(1, 1);
    private readonly Dictionary<string, PVPHostInfo> discoveredHosts = new(StringComparer.Ordinal);
    private readonly ObservableCollection<KeyValuePair<int, MemoryCard>> cards = [];
    private NetManager? manager;
    private EventBasedNetListener? listener;
    private NetPeer? peer;
    private PVPGameSettings? settings;
    private PVPPlayerRole localRole;
    private Guid sessionId;
    private PVPPlayerRole currentTurn;
    private int hostScore;
    private int clientScore;
    private int firstPosition = -1;
    private int secondPosition = -1;

    public PVPService(IThemeService themeService, ILogger<PVPService> logger)
    {
        this.themeService = themeService;
        this.logger = logger;
    }

    public IReadOnlyList<KeyValuePair<int, MemoryCard>> CurrentCards => cards;
    public Guid? SessionId => sessionId == Guid.Empty ? null : sessionId;
    public PVPPlayerRole LocalRole => localRole;
    public PVPPlayerRole CurrentTurn => currentTurn;
    public bool IsHost => localRole == PVPPlayerRole.Host;
    public bool IsConnected => peer?.ConnectionState == ConnectionState.Connected;
    public bool IsGameActive { get; private set; }
    public bool IsLocalTurn => IsGameActive && CurrentTurn == LocalRole;
    public int LocalScore => IsHost ? hostScore : clientScore;
    public int RemoteScore => IsHost ? clientScore : hostScore;

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
        await StopAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        settings = hostSettings with
        {
            Port = hostSettings.Port == 0 ? DefaultPort : hostSettings.Port,
            DiscoveryPort = hostSettings.DiscoveryPort == 0 ? DefaultDiscoveryPort : hostSettings.DiscoveryPort
        };
        localRole = PVPPlayerRole.Host;
        sessionId = Guid.NewGuid();
        await CreateBoardAsync(settings, cancellationToken);
        StartManager(settings.Port);
        logger.LogInformation("PVP host started on port {Port} for session {SessionId}", settings.Port, sessionId);
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
        var discoveryListener = new EventBasedNetListener();
        var discoveryManager = new NetManager(discoveryListener);
        discoveryListener.NetworkReceiveUnconnectedEvent += (endpoint, reader, _) =>
        {
            if (reader.AvailableBytes <= 0)
                return;

            try
            {
                PVPWireMessage? message = JsonSerializer.Deserialize<PVPWireMessage>(reader.GetString());
                if (message?.Type != PVPMessageType.DiscoveryResponse ||
                    message.ProtocolVersion != ProtocolVersion ||
                    message.Host is null)
                    return;

                lock (discoveredHosts)
                {
                    discoveredHosts[message.Host.Address + ":" + message.Host.Port] = message.Host;
                }
                HostDiscovered?.Invoke(this, new(message.Host));
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Ignoring malformed PVP discovery response from {Endpoint}", endpoint);
            }
        };

        if (!discoveryManager.Start(0))
            throw new InvalidOperationException("Could not start the PVP discovery socket.");

        var request = new PVPWireMessage(ProtocolVersion, PVPMessageType.DiscoveryRequest, null, null, null);
        SendUnconnected(discoveryManager, request, discoveryPort);
        logger.LogInformation("PVP discovery started on port {Port}", discoveryPort);

        try
        {
            DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken);
            }
        }
        finally
        {
            discoveryManager.Stop();
        }

        lock (discoveredHosts)
            result.AddRange(discoveredHosts.Values);
        return result;
    }

    public async Task ConnectAsync(PVPHostInfo host, string playerName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host.Address);
        await StopAsync(cancellationToken);
        settings = new PVPGameSettings(host.Difficulty, host.ThemeName, playerName, host.Port, host.DiscoveryPort);
        localRole = PVPPlayerRole.Client;
        sessionId = Guid.Empty;
        StartManager(0);
        logger.LogInformation("PVP client connecting to {Address}:{Port}", host.Address, host.Port);
        if (manager!.Connect(host.Address, host.Port, ProtocolVersion) is null)
            throw new InvalidOperationException($"Could not connect to PVP host {host.Address}:{host.Port}.");

        await WaitForConnectionAsync(cancellationToken);
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
            if (!IsGameActive || !IsLocalTurn || firstPosition >= 0 && secondPosition >= 0)
                return;
            if (cards.All(card => card.Key != position) ||
                cards.First(card => card.Key == position).Value.IsMatched ||
                cards.First(card => card.Key == position).Value.IsFaceUp)
                return;

            if (IsHost)
                ApplyAuthoritativeFlip(position, PVPPlayerRole.Host);
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
        peer?.Disconnect();
        manager?.Stop();
        manager = null;
        listener = null;
        peer = null;
        IsGameActive = false;
        sessionId = Guid.Empty;
        cards.Clear();
        firstPosition = -1;
        secondPosition = -1;
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        stateGate.Dispose();
    }

    private void StartManager(int port)
    {
        listener = new EventBasedNetListener();
        listener.PeerConnectedEvent += OnPeerConnected;
        listener.PeerDisconnectedEvent += OnPeerDisconnected;
        listener.NetworkReceiveEvent += OnNetworkReceive;
        listener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;
        listener.ConnectionRequestEvent += OnConnectionRequest;
        listener.NetworkErrorEvent += (_, error) => RaiseError($"PVP network error: {error}");
        manager = new NetManager(listener) { AutoRecycle = true };
        manager.Start(port);
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
                new PVPPlayerInfo(settings!.PlayerName, PVPPlayerRole.Client)));
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
        logger.LogInformation("PVP player disconnected from {Address}:{Port}", disconnectedPeer.Address, disconnectedPeer.Port);
        PlayerDisconnected?.Invoke(this, System.EventArgs.Empty);
    }

    private async void OnNetworkReceive(NetPeer sourcePeer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            string payload = reader.GetString();
            PVPWireMessage? message = JsonSerializer.Deserialize<PVPWireMessage>(payload);
            if (message is null || message.ProtocolVersion != ProtocolVersion)
                return;
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

            var host = new PVPHostInfo(settings.PlayerName, GetAdvertisedAddress(endpoint), settings.Port,
                settings.DiscoveryPort, settings.Difficulty, settings.ThemeName, peer is null ? 1 : 2, 2, ProtocolVersion);
            SendUnconnected(manager!, new PVPWireMessage(ProtocolVersion, PVPMessageType.DiscoveryResponse, sessionId, null, host),
                endpoint.Port);
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
            if (message.SessionId is not null && IsGameActive && message.SessionId != sessionId)
                return;
            switch (message.Type)
            {
                case PVPMessageType.ConnectionHello when IsHost:
                    break;
                case PVPMessageType.ConnectionAccepted:
                    break;
                case PVPMessageType.GameStart when !IsHost && message.GameState is not null:
                    await ApplyGameStateAsync(message.GameState);
                    PublishState(SessionStarted);
                    break;
                case PVPMessageType.CardFlipRequest when IsHost && message.CardMove is not null:
                    if (message.CardMove.Player == currentTurn)
                        ApplyAuthoritativeFlip(message.CardMove.Position, PVPPlayerRole.Client);
                    break;
                case PVPMessageType.CardFlipped when message.CardMove is not null:
                    ApplyRemoteFlip(message.CardMove.Position, message.CardMove.Player);
                    break;
                case PVPMessageType.TurnResolved when message.Resolution is not null:
                    ApplyResolution(message.Resolution);
                    break;
                case PVPMessageType.GameFinished when message.Result is not null:
                    IsGameActive = false;
                    GameFinished?.Invoke(this, new(message.Result));
                    break;
            }
        }
        finally
        {
            stateGate.Release();
        }
    }

    private void ApplyAuthoritativeFlip(int position, PVPPlayerRole player)
    {
        KeyValuePair<int, MemoryCard> entry = cards.First(card => card.Key == position);
        entry.Value.IsFaceUp = true;
        if (firstPosition < 0)
            firstPosition = position;
        else
            secondPosition = position;
        var move = new PVPCardMove(position, player);
        Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.CardFlipped, sessionId, null, move));
        CardChanged?.Invoke(this, new(position, player, false));
        if (secondPosition >= 0)
            ResolveAuthoritativeTurn(player);
    }

    private void ResolveAuthoritativeTurn(PVPPlayerRole player)
    {
        MemoryCard first = cards.First(card => card.Key == firstPosition).Value;
        MemoryCard second = cards.First(card => card.Key == secondPosition).Value;
        bool matched = first.PairId == second.PairId;
        if (matched)
        {
            first.IsMatched = true;
            second.IsMatched = true;
            if (player == PVPPlayerRole.Host) hostScore++; else clientScore++;
        }
        else
        {
            first.IsFaceUp = false;
            second.IsFaceUp = false;
        }

        CardChanged?.Invoke(this, new(firstPosition, player, matched));
        CardChanged?.Invoke(this, new(secondPosition, player, matched));
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
                hostScore, clientScore);
            Send(new PVPWireMessage(ProtocolVersion, PVPMessageType.GameFinished, sessionId, null, result));
            GameFinished?.Invoke(this, new(result));
        }
    }

    private void ApplyRemoteFlip(int position, PVPPlayerRole player)
    {
        if (cards.All(card => card.Key != position))
            return;
        cards.First(card => card.Key == position).Value.IsFaceUp = true;
        CardChanged?.Invoke(this, new(position, player, false));
    }

    private void ApplyResolution(PVPResolution resolution)
    {
        if (cards.All(card => card.Key != resolution.FirstPosition) ||
            cards.All(card => card.Key != resolution.SecondPosition))
            return;
        MemoryCard first = cards.First(card => card.Key == resolution.FirstPosition).Value;
        MemoryCard second = cards.First(card => card.Key == resolution.SecondPosition).Value;
        first.IsMatched = resolution.Matched;
        second.IsMatched = resolution.Matched;
        if (!resolution.Matched)
        {
            first.IsFaceUp = false;
            second.IsFaceUp = false;
        }
        hostScore = resolution.HostScore;
        clientScore = resolution.ClientScore;
        currentTurn = resolution.NextTurn;
        firstPosition = -1;
        secondPosition = -1;
        PublishState(TurnChanged);
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
        CardThemeDto theme = await themeService.GetThemeAsync(state.ThemeName)
            ?? throw new KeyNotFoundException($"Theme '{state.ThemeName}' was not found.");
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
        cards.Select(card => new PVPCardState(card.Key, card.Value.PairId, int.Parse(card.Value.PairId))).ToList();

    private void Send(PVPWireMessage message)
    {
        if (peer is null)
            return;
        var writer = new NetDataWriter();
        writer.Put(JsonSerializer.Serialize(message));
        peer.Send(writer, 0, DeliveryMethod.ReliableOrdered);
    }

    private static void SendUnconnected(NetManager networkManager, PVPWireMessage message, int port)
    {
        var writer = new NetDataWriter();
        writer.Put(JsonSerializer.Serialize(message));
        networkManager.SendBroadcast(writer, port);
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

    private static string GetAdvertisedAddress(IPEndPoint endpoint) =>
        endpoint.Address.Equals(IPAddress.Loopback) ? "127.0.0.1" : endpoint.Address.ToString();

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
