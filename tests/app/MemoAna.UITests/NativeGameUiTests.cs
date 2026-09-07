using Microsoft.Playwright;
using Xunit;
using Xunit.Sdk;

namespace MemoAna.UITests;

/// <summary>
/// Playwright connection used only with an already running MAUI WebView
/// automation endpoint. It never launches a standalone browser.
/// </summary>
public sealed class NativeAppFixture : IAsyncLifetime
{
    public IPlaywright? Playwright { get; private set; }
    public IBrowser? Browser { get; private set; }
    public IPage? Page { get; private set; }

    public async Task InitializeAsync()
    {
        string? endpoint = Environment.GetEnvironmentVariable("MEMOANA_NATIVE_CDP_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
            return;

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.ConnectOverCDPAsync(endpoint);
        IBrowserContext context = Browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("The native app exposed no browser context.");
        Page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.CloseAsync();
        Playwright?.Dispose();
    }
}

public sealed class NativeGameUiTests(NativeAppFixture fixture) : IClassFixture<NativeAppFixture>
{
    [Fact]
    [Trait("Target", "Android APK")]
    public Task AndroidIaTurnAndVisibleCards()
        => RunNativeScenarioAsync("Android APK");

    [Fact]
    [Trait("Target", "Windows EXE")]
    public Task WindowsIaTurnAndVisibleCards()
        => RunNativeScenarioAsync("Windows EXE");

    [Fact]
    [Trait("Target", "Android APK")]
    public Task AndroidPlayerVictoryCopy()
        => AssertResultCopyAsync("Android APK", "VOCÊ VENCEU!");

    [Fact]
    [Trait("Target", "Android APK")]
    public Task AndroidAiDefeatCopy()
        => AssertResultCopyAsync("Android APK", "VOCÊ PERDEU!");

    [Fact]
    [Trait("Target", "Windows EXE")]
    public Task WindowsPlayerVictoryCopy()
        => AssertResultCopyAsync("Windows EXE", "VOCÊ VENCEU!");

    [Fact]
    [Trait("Target", "Windows EXE")]
    public Task WindowsAiDefeatCopy()
        => AssertResultCopyAsync("Windows EXE", "VOCÊ PERDEU!");

    private async Task RunNativeScenarioAsync(string target)
    {
        if (fixture.Page is null)
            throw SkipException.ForSkip($"Configure MEMOANA_NATIVE_CDP_ENDPOINT for the running {target} app.");

        IPage page = fixture.Page;
        await page.GetByTestId("difficulty-easy").ClickAsync();
        await page.GetByTestId("mode-ai").ClickAsync();
        await page.GetByTestId("theme-select-button").First.ClickAsync();
        await ExpectEventuallyAsync(() => page.GetByTestId("turn-indicator").InnerTextAsync(), "SEU TURNO");

        ILocator cards = page.Locator("[data-testid^='card-']");
        await cards.Nth(0).ClickAsync();
        await ExpectEventuallyAsync(() => cards.Nth(0).Locator(".card-face").CountAsync(), 1);
        await cards.Nth(1).ClickAsync();
        await ExpectEventuallyAsync(() => cards.Nth(0).Locator(".card-face").CountAsync(), 1);
        await ExpectEventuallyAsync(() => cards.Nth(1).Locator(".card-face").CountAsync(), 1);

        // The AI must still own the turn when both of its cards are visible.
        // This observes the complete first-card -> interval -> second-card
        // sequence without introducing a fixed sleep into the test.
        await ExpectEventuallyAsync(async () =>
        {
            int visible = await page.Locator(".card-face").CountAsync();
            string turn = await page.GetByTestId("turn-indicator").InnerTextAsync().CatchAsync();
            return visible >= 2 && turn.Contains("IA", StringComparison.OrdinalIgnoreCase);
        }, true);

        await ExpectEventuallyAsync(() => page.GetByTestId("turn-indicator").InnerTextAsync(), "SEU TURNO");
    }

    private async Task AssertResultCopyAsync(string target, string expected)
    {
        if (fixture.Page is null)
            throw SkipException.ForSkip($"Configure MEMOANA_NATIVE_CDP_ENDPOINT for the running {target} app.");

        if (!string.Equals(Environment.GetEnvironmentVariable("MEMOANA_NATIVE_RESULT_SCENARIO"), expected, StringComparison.Ordinal))
            throw SkipException.ForSkip(
                $"Set MEMOANA_NATIVE_RESULT_SCENARIO to '{expected}' after arranging that native result scenario.");

        ILocator title = fixture.Page.GetByTestId("ia-result-title");
        await title.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ExpectEventuallyAsync(() => title.InnerTextAsync(), expected);
        await ExpectEventuallyAsync(
            () => fixture.Page!.GetByTestId("game-time").CountAsync(),
            0);
    }

    private static async Task ExpectEventuallyAsync(Func<Task<string>> value, string expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if ((await value()).Contains(expected, StringComparison.Ordinal))
                return;
            await Task.Delay(50);
        }
        Assert.Fail($"Expected UI value containing '{expected}'.");
    }

    private static async Task ExpectEventuallyAsync(Func<Task<int>> value, int expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await value() == expected)
                return;
            await Task.Delay(50);
        }
        Assert.Fail($"Expected UI value '{expected}'.");
    }

    private static async Task ExpectEventuallyAsync(Func<Task<bool>> value, bool expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await value() == expected)
                return;
            await Task.Delay(50);
        }
        Assert.Fail("Expected UI condition was not reached.");
    }
}

internal static class TaskExtensions
{
    public static async Task<string> CatchAsync(this Task<string> task)
    {
        try { return await task; }
        catch (PlaywrightException) { return string.Empty; }
    }
}
