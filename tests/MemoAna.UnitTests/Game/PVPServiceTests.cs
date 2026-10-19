using Microsoft.Extensions.Logging.Abstractions;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Models;
using MemoAna.Game.Services;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace MemoAna.UnitTests.Game;

public sealed class PVPServiceTests
{
    [Fact]
    public async Task HostAndClientSynchronizeGameAndAuthoritativeMatch()
    {
        SkipIfBrowser();
        (int port, int discoveryPort) = GetFreePortPair();
        await using var host = CreateService();
        await using var client = CreateService();

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, discoveryPort), TestContext.Current.CancellationToken);
        var hostInfo = new PVPHostInfo("Host", "127.0.0.1", port, discoveryPort, 0, "theme", 1, 2, "memoana-pvp-1");

        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionStarted += (_, _) => started.TrySetResult(true);

        await client.ConnectAsync(hostInfo, "Client", TestContext.Current.CancellationToken);
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

    private static PVPService CreateService() =>
        new(new FakeThemeService(), NullLogger<PVPService>.Instance);

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
        public Task<IReadOnlyCollection<CardThemeManifestDto>> GetThemesAsync() =>
            Task.FromResult<IReadOnlyCollection<CardThemeManifestDto>>([]);

        public Task<CardThemeDto> GetThemeAsync(string themeName) =>
            Task.FromResult(new CardThemeDto(
                [.. Enumerable.Range(1, 6).Select(index => $"image-{index}")],
                themeName,
                null));
    }
}
