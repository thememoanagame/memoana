using Xunit.Sdk;

namespace MemoAna.UITests;

public sealed class NativeGameUiTests
{
    [Fact]
    [Trait("Target", "Windows")]
    public Task WindowsTimeAttackAsync() =>
        RunSinglePlayerAsync("Windows", "mode-time-attack", "game-time",
            static configuration => new WindowsPlayerSession(configuration, "Windows"));

    [Fact]
    [Trait("Target", "Android")]
    public Task AndroidTimeAttackAsync() =>
        RunSinglePlayerAsync("Android", "mode-time-attack", "game-time",
            static configuration => new AndroidPlayerSession(configuration, "Android"));

    [Fact]
    [Trait("Target", "Windows")]
    public Task WindowsAiAsync() =>
        RunSinglePlayerAsync("Windows", "mode-ai", "turn-indicator",
            static configuration => new WindowsPlayerSession(configuration, "Windows"));

    [Fact]
    [Trait("Target", "Android")]
    public Task AndroidAiAsync() =>
        RunSinglePlayerAsync("Android", "mode-ai", "turn-indicator",
            static configuration => new AndroidPlayerSession(configuration, "Android"));

    [Fact]
    [Trait("Target", "Windows host / Android client")]
    public Task WindowsHostAndroidClientAsync() =>
        RunPvpAsync(hostIsWindows: true);

    [Fact]
    [Trait("Target", "Android host / Windows client")]
    public Task AndroidHostWindowsClientAsync() =>
        RunPvpAsync(hostIsWindows: false);

    private static async Task RunSinglePlayerAsync(
        string platform,
        string mode,
        string expectedElement,
        Func<NativeTestConfiguration, IPlayerSession> createSession)
    {
        NativeTestConfiguration configuration = NativeTestConfiguration.Load();
        if (platform == "Windows")
            configuration.ValidateWindows();
        else
            configuration.ValidateAndroid();

        await using IPlayerSession session = createSession(configuration);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
        await session.StartAsync(cancellation.Token);
        await session.OpenGameAsync(cancellation.Token);
        await session.WaitForTestIdAsync("mode-pvp", TimeSpan.FromSeconds(30), cancellation.Token);
        await session.ClickTestIdAsync("difficulty-easy", cancellation.Token);
        await session.ClickTestIdAsync(mode, cancellation.Token);
        await session.ClickTestIdAsync("theme-select-button", cancellation.Token);
        await session.WaitForTestIdAsync(expectedElement, TimeSpan.FromSeconds(30), cancellation.Token);
        IReadOnlyList<string> cards = await session.ReadCardTestIdsAsync(cancellation.Token);
        Assert.NotEmpty(cards);
    }

    private static async Task RunPvpAsync(bool hostIsWindows)
    {
        NativeTestConfiguration configuration = NativeTestConfiguration.Load();
        configuration.ValidateWindows();
        configuration.ValidateAndroid();
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(5));

        await using IPlayerSession windows = new WindowsPlayerSession(configuration, "Windows");
        await using IPlayerSession android = new AndroidPlayerSession(configuration, "Android");
        await Task.WhenAll(
            windows.StartAsync(cancellation.Token),
            android.StartAsync(cancellation.Token));
        await Task.WhenAll(
            windows.OpenGameAsync(cancellation.Token),
            android.OpenGameAsync(cancellation.Token));
        await Task.WhenAll(
            windows.WaitForTestIdAsync("mode-pvp", TimeSpan.FromSeconds(30), cancellation.Token),
            android.WaitForTestIdAsync("mode-pvp", TimeSpan.FromSeconds(30), cancellation.Token));

        IPlayerSession host = hostIsWindows ? windows : android;
        IPlayerSession client = hostIsWindows ? android : windows;
        await host.ClickTestIdAsync("difficulty-easy", cancellation.Token);
        await host.ClickTestIdAsync("mode-pvp", cancellation.Token);
        await host.ClickTestIdAsync("theme-select-button", cancellation.Token);
        await host.WaitForTestIdAsync("create-pvp-room", TimeSpan.FromSeconds(20), cancellation.Token);
        await host.ClickTestIdAsync("create-pvp-room", cancellation.Token);
        await client.ClickTestIdAsync("difficulty-easy", cancellation.Token);
        await client.ClickTestIdAsync("mode-pvp", cancellation.Token);
        await client.ClickTestIdAsync("theme-select-button", cancellation.Token);
        await client.WaitForTestIdAsync("discover-pvp-rooms", TimeSpan.FromSeconds(20), cancellation.Token);
        await client.ClickTestIdAsync("discover-pvp-rooms", cancellation.Token);
        await client.WaitForTestIdAsync("pvp-room", TimeSpan.FromSeconds(30), cancellation.Token);
        await client.ClickTestIdAsync("pvp-room", cancellation.Token);
        await host.WaitForTestIdAsync("start-pvp-game", TimeSpan.FromSeconds(30), cancellation.Token);
        await host.ClickTestIdAsync("start-pvp-game", cancellation.Token);
        await host.WaitForTestIdAsync("turn-indicator", TimeSpan.FromSeconds(30), cancellation.Token);
        await client.WaitForTestIdAsync("turn-indicator", TimeSpan.FromSeconds(30), cancellation.Token);

        IReadOnlyList<string> hostCards = await host.ReadCardTestIdsAsync(cancellation.Token);
        IReadOnlyList<string> clientCards = await client.ReadCardTestIdsAsync(cancellation.Token);
        Assert.Equal(hostCards.Count, clientCards.Count);
        Assert.NotEmpty(hostCards);
    }
}
