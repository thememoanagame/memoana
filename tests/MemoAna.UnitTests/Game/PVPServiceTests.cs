using System.Linq.Expressions;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Entities;
using MemoAna.Common.Enums;
using MemoAna.Common.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.ApplicationModel;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;
using MemoAna.Game.Models;
using MemoAna.Game.Services;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace MemoAna.UnitTests.Game;

public sealed class PVPServiceTests
{
    [Fact]
    public async Task StartHostRaisesHostStartedOnceAndStartsBothManagers()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();
        int eventCount = 0;
        host.HostStarted += (_, _) => eventCount++;

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, eventCount);
        Assert.True(host.IsHost);
        Assert.NotNull(host.SessionId);
        Assert.NotEmpty(host.CurrentCards);
        Assert.False(host.IsConnected);
        Assert.Equal(port, Assert.Single(await client.DiscoverHostsAsync(
            discoveryPort, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)).Port);
    }

    [Fact]
    public async Task HostAndClientSynchronizeGameAndAuthoritativeMatch()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Alice", port, discoveryPort), TestContext.Current.CancellationToken);
        var hostInfo = new PVPHostInfo("Alice", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1");

        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);

        await client.ConnectAsync(hostInfo, "Bob", TestContext.Current.CancellationToken);
        Assert.True(host.IsConnected);
        await host.StartGameAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(host.CurrentCards.Count, client.CurrentCards.Count);
        Assert.Equal(host.CurrentCards.Select(card => card.Value.PairId),
            client.CurrentCards.Select(card => card.Value.PairId));
        Assert.True(host.IsLocalTurn);
        Assert.False(client.IsLocalTurn);

        TaskCompletionSource<bool> resolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> clientResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.TurnChanged += OnTurnChanged;
        client.TurnChanged += OnClientTurnChanged;
        KeyValuePair<int, MemoAna.Game.Core.MemoryCard>[] pair = [.. host.CurrentCards
            .GroupBy(card => card.Value.PairId).First()];
        await host.FlipCardAsync(pair[0].Key, TestContext.Current.CancellationToken);
        await host.FlipCardAsync(pair[1].Key, TestContext.Current.CancellationToken);
        await resolved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await clientResolved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        host.TurnChanged -= OnTurnChanged;
        client.TurnChanged -= OnClientTurnChanged;

        Assert.True(host.CurrentCards.First(card => card.Key == pair[0].Key).Value.IsMatched);
        Assert.True(client.CurrentCards.First(card => card.Key == pair[1].Key).Value.IsMatched);
        Assert.Equal(host.LocalScore, client.RemoteScore);
        Assert.Equal(new PVPPlayerStatistics(PVPPlayerRole.Host, "Alice", 1, 1, 0, 1), host.LocalStatistics);
        Assert.Equal("Bob", host.RemotePlayerName);
        Assert.Equal("Alice", client.RemotePlayerName);
        Assert.True(host.IsLocalTurn);

        void OnTurnChanged(object? sender, PVPStateChangedEventArgs args) => resolved.TrySetResult(true);
        void OnClientTurnChanged(object? sender, PVPStateChangedEventArgs args) => clientResolved.TrySetResult(true);
    }

    [Fact]
    public async Task HostCanBeDiscoveredByClientWithoutAConnection()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();
        TaskCompletionSource<PVPHostInfo> discovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.HostDiscovered += (_, args) => discovered.TrySetResult(args.Host);

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort), TestContext.Current.CancellationToken);
        IReadOnlyList<PVPHostInfo> hosts = await client.DiscoverHostsAsync(
            discoveryPort, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        PVPHostInfo result = Assert.Single(hosts);
        Assert.Equal("Host", result.HostName);
        Assert.False(string.IsNullOrWhiteSpace(result.Address));
        Assert.Equal(port, result.Port);
        Assert.Equal(discoveryPort, result.DiscoveryPort);
        Assert.Equal("theme", result.ThemeName);
        Assert.Equal(0, result.Difficulty);
        Assert.Equal("memoana-pvp-1", result.ProtocolVersion);
        Assert.Equal(1, result.ConnectedPlayers);
        Assert.Equal(2, result.MaxPlayers);
        Assert.Equal(result, await discovered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.False(client.IsConnected);

        await client.ConnectAsync(result, "Client", TestContext.Current.CancellationToken);
        Assert.True(client.IsConnected);
        Assert.True(host.IsConnected);
    }

    [Fact]
    public async Task HostCanStopAndStartAgainWithFreshSessionAndDiscovery()
    {
        SkipIfBrowser();
        (int firstPort, int firstDiscoveryPort) = GetFreePortPair();
        (int secondPort, int secondDiscoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", firstPort, firstDiscoveryPort), TestContext.Current.CancellationToken);
        Guid firstSession = Assert.IsType<Guid>(host.SessionId);
        await host.StopAsync(TestContext.Current.CancellationToken);
        Assert.Null(host.SessionId);
        Assert.Empty(host.CurrentCards);

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", secondPort, secondDiscoveryPort), TestContext.Current.CancellationToken);
        Assert.NotEqual(firstSession, host.SessionId);
        IReadOnlyList<PVPHostInfo> hosts = await client.DiscoverHostsAsync(
            secondDiscoveryPort, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(secondPort, Assert.Single(hosts).Port);
    }

    [Fact]
    public async Task HostStopRaisesPlayerDisconnectedAndClientClearsSession()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();
        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort),
            TestContext.Current.CancellationToken);
        PVPHostInfo discovered = Assert.Single(await client.DiscoverHostsAsync(
            discoveryPort, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        await client.ConnectAsync(discovered, "Client", TestContext.Current.CancellationToken);

        TaskCompletionSource<bool> disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PlayerDisconnected += (_, _) => disconnected.TrySetResult(true);
        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);
        await host.StartGameAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(client.IsConnected);
        Assert.False(client.IsGameActive);
        Assert.Null(client.SessionId);
        Assert.Empty(client.CurrentCards);
    }

    [Fact]
    public async Task StopDisconnectsAndClearsSession()
    {
        SkipIfBrowser();
        await using var service = CreateService();
        await service.StartHostAsync(new PVPGameSettings(0, "theme", "Host", 0, 0), TestContext.Current.CancellationToken);

        Assert.True(service.IsHost);
        Assert.NotNull(service.SessionId);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(service.IsConnected);
        Assert.False(service.IsGameActive);
        Assert.Null(service.SessionId);
        Assert.Empty(service.CurrentCards);
    }

    [Fact]
    public async Task GameCompletionPersistsBothPlayersExactlyOnce()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        var repository = new RecordingStatisticsRepository();
        await using var host = CreateService(repository);
        await using var client = CreateService();

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Alice", port, discoveryPort), TestContext.Current.CancellationToken);
        await client.ConnectAsync(new PVPHostInfo("Alice", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1"), "Bob", TestContext.Current.CancellationToken);
        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);
        TaskCompletionSource<bool> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.GameFinished += (_, _) => finished.TrySetResult(true);
        await host.StartGameAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        foreach (KeyValuePair<int, MemoAna.Game.Core.MemoryCard>[] pair in host.CurrentCards.GroupBy(card => card.Value.PairId).Select(group => group.ToArray()))
        {
            await host.FlipCardAsync(pair[0].Key, TestContext.Current.CancellationToken);
            await host.FlipCardAsync(pair[1].Key, TestContext.Current.CancellationToken);
        }

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, repository.Items.Count);
        Assert.Equal(["Alice", "Bob"], repository.Items.Select(item => item.PlayerName).OrderBy(name => name));
        Assert.All(repository.Items, item => Assert.Equal(MemoAna.Game.Enums.GameMode.PVP, item.GameMode));
    }

    [Fact]
    public async Task StartHostAsync_RequestsAndEnsuresLocalNetworkBeforeSocket()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        var permission = new FakePermissionService { StatusToReturn = PermissionStatus.Granted };
        await using var host = CreateService(permissionService: permission);

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort), TestContext.Current.CancellationToken);

        Assert.Contains(AppPermission.LocalNetwork, permission.EnsureGrantedCalls);
        Assert.True(host.IsHost);
    }

    [Fact]
    public async Task DiscoverHostsAsync_RequestsAndEnsuresLocalNetworkBeforeDiscovery()
    {
        SkipIfBrowser();
        (int _, int discoveryPort) = GetFreePortPair();
        var permission = new FakePermissionService { StatusToReturn = PermissionStatus.Granted };
        await using var client = CreateService(permissionService: permission);

        IReadOnlyList<PVPHostInfo> hosts = await client.DiscoverHostsAsync(
            discoveryPort, TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.Contains(AppPermission.LocalNetwork, permission.EnsureGrantedCalls);
        Assert.NotNull(hosts);
    }

    [Fact]
    public async Task ConnectAsync_RequestsAndEnsuresLocalNetworkBeforeConnection()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort), TestContext.Current.CancellationToken);

        var permission = new FakePermissionService { StatusToReturn = PermissionStatus.Granted };
        await using var client = CreateService(permissionService: permission);
        var hostInfo = new PVPHostInfo("Host", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1");

        await client.ConnectAsync(hostInfo, "Client", TestContext.Current.CancellationToken);

        Assert.Contains(AppPermission.LocalNetwork, permission.EnsureGrantedCalls);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task StartHostAsync_WhenPermissionDenied_DoesNotStartSocketAndThrowsException()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        var permission = new FakePermissionService
        {
            StatusToReturn = PermissionStatus.Denied,
            RequestStatusToReturn = PermissionStatus.Denied
        };
        await using var host = CreateService(permissionService: permission);

        PVPErrorEventArgs? errorEvent = null;
        host.Error += (_, e) => errorEvent = e;

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort), TestContext.Current.CancellationToken));

        Assert.Equal(AppPermission.LocalNetwork, ex.Permission);
        Assert.Equal(PermissionStatus.Denied, ex.Status);
        Assert.Null(host.SessionId);
        Assert.False(host.IsGameActive);
        Assert.False(host.IsConnected);
        Assert.Empty(host.CurrentCards);
        Assert.NotNull(errorEvent);
    }

    [Fact]
    public async Task DiscoverHostsAsync_WhenPermissionDenied_DoesNotStartDiscoveryAndThrowsException()
    {
        SkipIfBrowser();
        (int _, int discoveryPort) = GetFreePortPair();
        var permission = new FakePermissionService
        {
            StatusToReturn = PermissionStatus.Denied,
            RequestStatusToReturn = PermissionStatus.Denied
        };
        await using var client = CreateService(permissionService: permission);

        PVPErrorEventArgs? errorEvent = null;
        client.Error += (_, e) => errorEvent = e;

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            client.DiscoverHostsAsync(discoveryPort, TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));

        Assert.Equal(AppPermission.LocalNetwork, ex.Permission);
        Assert.Equal(PermissionStatus.Denied, ex.Status);
        Assert.NotNull(errorEvent);
    }

    [Fact]
    public async Task ConnectAsync_WhenPermissionDenied_DoesNotStartConnectionAndThrowsException()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        var permission = new FakePermissionService
        {
            StatusToReturn = PermissionStatus.Denied,
            RequestStatusToReturn = PermissionStatus.Denied
        };
        await using var client = CreateService(permissionService: permission);
        var hostInfo = new PVPHostInfo("Host", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1");

        PVPErrorEventArgs? errorEvent = null;
        client.Error += (_, e) => errorEvent = e;

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            client.ConnectAsync(hostInfo, "Client", TestContext.Current.CancellationToken));

        Assert.Equal(AppPermission.LocalNetwork, ex.Permission);
        Assert.Equal(PermissionStatus.Denied, ex.Status);
        Assert.False(client.IsConnected);
        Assert.NotNull(errorEvent);
    }

    [Fact]
    public async Task Pvp_WhenPermissionAlreadyGranted_DoesNotRequestAgain()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        var permission = new FakePermissionService
        {
            StatusToReturn = PermissionStatus.Granted,
            RequestStatusToReturn = PermissionStatus.Granted
        };
        await using var host = CreateService(permissionService: permission);

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort), TestContext.Current.CancellationToken);

        Assert.Contains(AppPermission.LocalNetwork, permission.EnsureGrantedCalls);
        Assert.Empty(permission.RequestCalls);
    }

    private static PVPService CreateService(
        IRepository<GameStatisticsEntity>? repository = null,
        IPermissionService? permissionService = null) =>
        new(new FakeThemeService(), NullLogger<PVPService>.Instance, permissionService ?? new FakePermissionService(), repository);

    private sealed class FakePermissionService : IPermissionService
    {
        public PermissionStatus StatusToReturn { get; set; } = PermissionStatus.Granted;
        public PermissionStatus RequestStatusToReturn { get; set; } = PermissionStatus.Granted;
        public List<AppPermission> EnsureGrantedCalls { get; } = [];
        public List<AppPermission> CheckStatusCalls { get; } = [];
        public List<AppPermission> RequestCalls { get; } = [];

        public Task<PermissionStatus> CheckStatusAsync(AppPermission permission, CancellationToken cancellationToken = default)
        {
            CheckStatusCalls.Add(permission);
            return Task.FromResult(StatusToReturn);
        }

        public Task<PermissionStatus> RequestAsync(AppPermission permission, CancellationToken cancellationToken = default)
        {
            RequestCalls.Add(permission);
            return Task.FromResult(RequestStatusToReturn);
        }

        public Task<PermissionStatus> EnsureGrantedAsync(AppPermission permission, CancellationToken cancellationToken = default)
        {
            EnsureGrantedCalls.Add(permission);
            if (StatusToReturn == PermissionStatus.Granted)
                return Task.FromResult(PermissionStatus.Granted);

            RequestCalls.Add(permission);
            return Task.FromResult(RequestStatusToReturn);
        }

        public Task<bool> IsGrantedAsync(AppPermission permission, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StatusToReturn == PermissionStatus.Granted);
        }
    }

    private static (int Port, int DiscoveryPort) GetFreePortPair()
    {
        using var game = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var discovery = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int gamePort = ((IPEndPoint)game.Client.LocalEndPoint!).Port;
        int discoveryPort = ((IPEndPoint)discovery.Client.LocalEndPoint!).Port;
        return gamePort == discoveryPort ? GetFreePortPair() : (gamePort, discoveryPort);
    }

    private static void SkipIfBrowser()
    {
        if (OperatingSystem.IsBrowser())
            Assert.Skip("LiteNetLib requires System.Net.Sockets and cannot run in the WebAssembly test host.");
    }

    private sealed class FakeThemeService : IThemeService
    {
        public Task<IReadOnlyCollection<CardThemeManifestDto>> GetThemesAsync(bool include = false) =>
            Task.FromResult<IReadOnlyCollection<CardThemeManifestDto>>([]);

        public Task<CardThemeDto> GetThemeAsync(string themeName) =>
            Task.FromResult(new CardThemeDto(
                [.. Enumerable.Range(1, 6).Select(index => $"image-{index}")],
                themeName,
                null));
    }

    private sealed class RecordingStatisticsRepository : IRepository<GameStatisticsEntity>
    {
        public List<GameStatisticsEntity> Items { get; } = [];
        public Task AddAsync(GameStatisticsEntity value, CancellationToken cancellationToken) { Items.Add(value); return Task.CompletedTask; }
        public Task<GameStatisticsEntity?> GetByIdAsync(string id, Expression<Func<GameStatisticsEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult<GameStatisticsEntity?>(null);
        public Task<GameStatisticsEntity?> GetTrackedByIdAsync(string id, Expression<Func<GameStatisticsEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult<GameStatisticsEntity?>(null);
        public Task<IReadOnlyCollection<GameStatisticsEntity?>> ListTrackedAsync(Expression<Func<GameStatisticsEntity, bool>>? predicate, Expression<Func<GameStatisticsEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GameStatisticsEntity?>>([]);
        public Task<GameStatisticsEntity?> FirstOrDefaultAsync(Expression<Func<GameStatisticsEntity, bool>> predicate, Expression<Func<GameStatisticsEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult<GameStatisticsEntity?>(null);
        public Task<IReadOnlyList<GameStatisticsEntity>> ListAsync(Expression<Func<GameStatisticsEntity, bool>>? predicate, Expression<Func<GameStatisticsEntity, object?>>[] includes, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameStatisticsEntity>>([]);
        public Task<bool> ExistsAsync(Expression<Func<GameStatisticsEntity, bool>> predicate, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task UpdateAsync(GameStatisticsEntity value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(GameStatisticsEntity value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveRangeAsync(IEnumerable<GameStatisticsEntity> values, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task HostAndClient_BothReceiveGameFinished_WithFinalStateAndStats()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        var hostRepo = new RecordingStatisticsRepository();
        var clientRepo = new RecordingStatisticsRepository();
        await using var host = CreateService(hostRepo);
        await using var client = CreateService(clientRepo);

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Alice", port, discoveryPort), TestContext.Current.CancellationToken);
        await client.ConnectAsync(new PVPHostInfo("Alice", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1"), "Bob", TestContext.Current.CancellationToken);

        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);
        TaskCompletionSource<PVPGameResult> hostFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<PVPGameResult> clientFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.GameFinished += (_, args) => hostFinished.TrySetResult(args.Result);
        client.GameFinished += (_, args) => clientFinished.TrySetResult(args.Result);

        await host.StartGameAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        foreach (KeyValuePair<int, MemoAna.Game.Core.MemoryCard>[] pair in host.CurrentCards.GroupBy(card => card.Value.PairId).Select(group => group.ToArray()))
        {
            await host.FlipCardAsync(pair[0].Key, TestContext.Current.CancellationToken);
            await host.FlipCardAsync(pair[1].Key, TestContext.Current.CancellationToken);
        }

        PVPGameResult hostResult = await hostFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        PVPGameResult clientResult = await clientFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Verify Host State
        Assert.False(host.IsGameActive);
        Assert.NotNull(host.SessionId);
        Assert.Equal(host.SessionId, hostResult.SessionId);
        Assert.Equal(PVPPlayerRole.Host, hostResult.Winner);
        Assert.Equal(6, host.LocalStatistics.Hits);
        Assert.Equal(0, host.LocalStatistics.Misses);

        // Verify Client State
        Assert.False(client.IsGameActive);
        Assert.NotNull(client.SessionId);
        Assert.Equal(host.SessionId, client.SessionId);
        Assert.Equal(host.SessionId, clientResult.SessionId);
        Assert.Equal(6, client.RemoteStatistics.Hits);
        Assert.Equal("Alice", client.RemotePlayerName);
        Assert.Equal("Bob", client.LocalPlayerName);
    }

    [Fact]
    public async Task GameCompletion_IsIdempotent_SecondFinishMessageIgnored()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        var clientRepo = new RecordingStatisticsRepository();
        await using var host = CreateService();
        await using var client = CreateService(clientRepo);

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Alice", port, discoveryPort), TestContext.Current.CancellationToken);
        await client.ConnectAsync(new PVPHostInfo("Alice", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1"), "Bob", TestContext.Current.CancellationToken);

        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);
        int clientFinishCount = 0;
        PVPGameResult? lastResult = null;
        client.GameFinished += (_, args) =>
        {
            clientFinishCount++;
            lastResult = args.Result;
        };

        await host.StartGameAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        foreach (KeyValuePair<int, MemoAna.Game.Core.MemoryCard>[] pair in host.CurrentCards.GroupBy(card => card.Value.PairId).Select(group => group.ToArray()))
        {
            await host.FlipCardAsync(pair[0].Key, TestContext.Current.CancellationToken);
            await host.FlipCardAsync(pair[1].Key, TestContext.Current.CancellationToken);
        }

        // Wait until first finish completes
        for (int i = 0; i < 50 && clientFinishCount == 0; i++)
            await Task.Delay(50);

        Assert.Equal(1, clientFinishCount);
        int initialPersistedCount = clientRepo.Items.Count;
        Assert.True(initialPersistedCount > 0);

        // Simulate duplicate wire message arriving at client
        var wireMessageType = typeof(PVPService).GetNestedType("PVPWireMessage", System.Reflection.BindingFlags.NonPublic)!;
        var wireMessage = Activator.CreateInstance(wireMessageType, "memoana-pvp-1", PVPMessageType.GameFinished, client.SessionId, null, lastResult);
        var handleMethod = typeof(PVPService).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)handleMethod.Invoke(client, [wireMessage])!;

        // Event and persistence should NOT duplicate
        Assert.Equal(1, clientFinishCount);
        Assert.Equal(initialPersistedCount, clientRepo.Items.Count);
    }

    [Fact]
    public async Task OldSessionFinishMessage_DoesNotEndCurrentGame()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Alice", port, discoveryPort), TestContext.Current.CancellationToken);
        await client.ConnectAsync(new PVPHostInfo("Alice", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1"), "Bob", TestContext.Current.CancellationToken);

        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);
        bool clientFinishedCalled = false;
        client.GameFinished += (_, _) => clientFinishedCalled = true;

        await host.StartGameAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(client.IsGameActive);

        // Send GameFinished with a DIFFERENT sessionId
        Guid oldSessionId = Guid.NewGuid();
        var staleResult = new PVPGameResult(oldSessionId, PVPPlayerRole.Host, 10, 0, "theme", 0, DateTime.UtcNow, []);
        var wireMessageType = typeof(PVPService).GetNestedType("PVPWireMessage", System.Reflection.BindingFlags.NonPublic)!;
        var wireMessage = Activator.CreateInstance(wireMessageType, "memoana-pvp-1", PVPMessageType.GameFinished, oldSessionId, null, staleResult);

        var handleMethod = typeof(PVPService).GetMethod("HandleMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)handleMethod.Invoke(client, [wireMessage])!;

        Assert.True(client.IsGameActive);
        Assert.False(clientFinishedCalled);
        Assert.NotEqual(oldSessionId, client.SessionId);
    }

    [Fact]
    public async Task PeerDisconnectAfterGameCompletion_PreservesSessionIdAndStatistics()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Alice", port, discoveryPort), TestContext.Current.CancellationToken);
        await client.ConnectAsync(new PVPHostInfo("Alice", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1"), "Bob", TestContext.Current.CancellationToken);

        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);
        TaskCompletionSource<bool> clientFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GameFinished += (_, _) => clientFinished.TrySetResult(true);

        await host.StartGameAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        foreach (KeyValuePair<int, MemoAna.Game.Core.MemoryCard>[] pair in host.CurrentCards.GroupBy(card => card.Value.PairId).Select(group => group.ToArray()))
        {
            await host.FlipCardAsync(pair[0].Key, TestContext.Current.CancellationToken);
            await host.FlipCardAsync(pair[1].Key, TestContext.Current.CancellationToken);
        }

        await clientFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Guid expectedSessionId = Assert.IsType<Guid>(client.SessionId);

        TaskCompletionSource<bool> clientDisconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PlayerDisconnected += (_, _) => clientDisconnected.TrySetResult(true);

        // Host disconnects after the match has finished
        await host.StopAsync(TestContext.Current.CancellationToken);
        await clientDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Client must retain its finished session ID and statistics!
        Assert.False(client.IsConnected);
        Assert.False(client.IsGameActive);
        Assert.Equal(expectedSessionId, client.SessionId);
        Assert.Equal(6, client.RemoteStatistics.Hits);
        Assert.NotEmpty(client.CurrentCards);
    }
}
