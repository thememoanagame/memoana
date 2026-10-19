using Microsoft.Extensions.Logging.Abstractions;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Models;
using MemoAna.Game.Services;
using Xunit;

namespace MemoAna.UnitTests.Game;

public sealed class PVPServiceTests
{
    [Fact]
    public async Task HostAndClientSynchronizeGameAndAuthoritativeMatch()
    {
        SkipIfBrowser();
        int port = 38000 + Random.Shared.Next(500);
        await using var host = CreateService();
        await using var client = CreateService();

        await host.StartHostAsync(new PVPGameSettings(0, "theme", "Host", port, port + 1), TestContext.Current.CancellationToken);
        var hostInfo = new PVPHostInfo("Host", "127.0.0.1", port, port + 1, 0, "theme", 1, 2, "memoana-pvp-1");

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

        KeyValuePair<int, MemoAna.Game.Core.MemoryCard>[] pair = [.. host.CurrentCards
            .GroupBy(card => card.Value.PairId).First()];
        await host.FlipCardAsync(pair[0].Key, TestContext.Current.CancellationToken);
        await host.FlipCardAsync(pair[1].Key, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(host.CurrentCards.First(card => card.Key == pair[0].Key).Value.IsMatched);
        Assert.True(client.CurrentCards.First(card => card.Key == pair[1].Key).Value.IsMatched);
        Assert.Equal(host.LocalScore, client.RemoteScore);
        Assert.True(host.IsLocalTurn);
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
