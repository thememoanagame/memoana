using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace MemoAna.UITests;

internal interface IPlayerSession : IAsyncDisposable
{
    string PlayerName { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task OpenGameAsync(CancellationToken cancellationToken);
    Task ClickTestIdAsync(string testId, CancellationToken cancellationToken);
    Task<string> ReadTestIdAsync(string testId, CancellationToken cancellationToken);
    Task WaitForTestIdAsync(string testId, TimeSpan timeout, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadCardTestIdsAsync(CancellationToken cancellationToken);
}

internal sealed class WindowsPlayerSession(
    NativeTestConfiguration configuration,
    string playerName) : IPlayerSession
{
    private readonly int debugPort = GetFreePort();
    private Process? process;
    private IPlaywright? playwright;
    private IBrowser? browser;
    private IPage? page;

    public string PlayerName => playerName;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        configuration.ValidateWindows();
        process = Process.Start(new ProcessStartInfo(configuration.WindowsExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(configuration.WindowsExecutable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                ["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
                    $"--remote-debugging-port={debugPort}",
                ["WEBVIEW2_USER_DATA_FOLDER"] = Path.Combine(
                    configuration.ArtifactDirectory,
                    $"{PlayerName}-webview-{Guid.NewGuid():N}")
            }
        }) ?? throw new InvalidOperationException(
            $"Could not start the Windows app for {PlayerName}.");

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(1) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        bool debugPortReady = false;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _ = await client.GetFromJsonAsync<JsonElement>(
                    $"http://127.0.0.1:{debugPort}/json/version",
                    cancellationToken);
                debugPortReady = true;
                break;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
        if (!debugPortReady)
            throw new InvalidOperationException(
                $"Windows app {PlayerName} did not expose WebView2 CDP on port {debugPort}.");

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.ConnectOverCDPAsync(
            $"http://127.0.0.1:{debugPort}");
        IBrowserContext context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Windows app {PlayerName} exposed no WebView2 context.");
        page = context.Pages.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Windows app {PlayerName} exposed no WebView2 page.");
        try
        {
            await Page().Locator("a[href='/game']").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30000
            });
        }
        catch (TimeoutException)
        {
            string diagnostic = $"URL: {page.Url}{Environment.NewLine}" +
                $"Title: {await page.TitleAsync()}{Environment.NewLine}" +
                await page.ContentAsync();
            await File.WriteAllTextAsync(
                Path.Combine(configuration.ArtifactDirectory, $"{PlayerName}-startup.html"),
                diagnostic,
                cancellationToken);
            throw;
        }
    }

    public async Task ClickTestIdAsync(string testId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Page().GetByTestId(testId).ClickAsync();
    }

    public async Task OpenGameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Page().Locator("a[href='/game']").ClickAsync();
    }

    public async Task<string> ReadTestIdAsync(string testId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Page().GetByTestId(testId).InnerTextAsync();
    }

    public async Task WaitForTestIdAsync(string testId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await Page().GetByTestId(testId).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = (float)timeout.TotalMilliseconds
        });
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<IReadOnlyList<string>> ReadCardTestIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Page().Locator("[data-testid^='card-']").EvaluateAllAsync<string[]>(
            "cards => cards.map(card => card.getAttribute('data-testid') ?? '')");
    }

    public async ValueTask DisposeAsync()
    {
        if (browser is not null)
            await browser.CloseAsync();
        playwright?.Dispose();
        if (process is { HasExited: false })
            process.Kill(entireProcessTree: true);
        process?.Dispose();
    }

    private IPage Page() => page
        ?? throw new InvalidOperationException($"Windows session {PlayerName} is not started.");

    private static int GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

internal sealed class AndroidPlayerSession(
    NativeTestConfiguration configuration,
    string playerName) : IPlayerSession
{
    private AndroidDriver? driver;
    private Process? appium;

    public string PlayerName => playerName;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        configuration.ValidateAndroid();
        string serial = await NativePrerequisites.SelectAndroidDeviceAsync(
            configuration.AndroidDeviceSerial, cancellationToken);
        await InstallAsync(configuration.AndroidApk, serial, cancellationToken);
        appium = Process.Start(new ProcessStartInfo(configuration.AppiumCommand,
            $"--address 127.0.0.1 --port {Port(configuration.AppiumUrl)}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start Appium.");
        using (HttpClient client = new() { Timeout = TimeSpan.FromSeconds(1) })
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            bool serverReady = false;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using HttpResponseMessage response = await client.GetAsync(
                        $"{configuration.AppiumUrl.TrimEnd('/')}/status",
                        cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        serverReady = true;
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                await Task.Delay(250, cancellationToken);
            }
            if (!serverReady)
                throw new InvalidOperationException(
                    $"Appium did not become ready at {configuration.AppiumUrl}. " +
                    $"Command: {configuration.AppiumCommand}");
        }

        AppiumOptions options = new();
        options.PlatformName = "Android";
        options.AutomationName = "UiAutomator2";
        options.DeviceName = serial;
        options.App = configuration.AndroidApk;
        options.AddAdditionalAppiumOption("udid", serial);
        options.AddAdditionalAppiumOption("appPackage", configuration.AndroidPackage);
        driver = new AndroidDriver(new Uri(configuration.AppiumUrl), options,
            TimeSpan.FromSeconds(60));
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(2);
        DateTime webViewDeadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < webViewDeadline)
        {
            string? webView = driver.Contexts.FirstOrDefault(
                context => context.StartsWith("WEBVIEW", StringComparison.Ordinal));
            if (webView is not null)
            {
                driver.Context = webView;
                break;
            }
            await Task.Delay(250, cancellationToken);
        }
        if (!driver.Context.StartsWith("WEBVIEW", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Android player {PlayerName} did not expose a debuggable WebView.");
        DateTime gameDeadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < gameDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Element("start-game").Displayed)
                    return;
            }
            catch (WebDriverException)
            {
                try
                {
                    if (driver!.FindElement(By.CssSelector("a[href='/game']")).Displayed)
                        return;
                }
                catch (WebDriverException)
                {
                }
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException($"Android player {PlayerName} did not expose the game link.");
    }

    public Task ClickTestIdAsync(string testId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Element(testId).Click();
        return Task.CompletedTask;
    }

    public Task OpenGameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        driver!.FindElement(By.CssSelector("a[href='/game']")).Click();
        return Task.CompletedTask;
    }

    public Task<string> ReadTestIdAsync(string testId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Element(testId).Text);
    }

    public async Task WaitForTestIdAsync(string testId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Element(testId).Displayed)
                    return;
            }
            catch (WebDriverException)
            {
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException(
            $"Android player {PlayerName} did not expose '{testId}' within {timeout}.");
    }

    public Task<IReadOnlyList<string>> ReadCardTestIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> ids = driver!.FindElements(By.CssSelector("[data-testid^='card-']"))
            .Select(element => element.GetAttribute("data-testid") ?? string.Empty)
            .ToArray();
        return Task.FromResult(ids);
    }

    public ValueTask DisposeAsync()
    {
        try { driver?.Quit(); } finally { driver?.Dispose(); }
        if (appium is { HasExited: false })
            appium.Kill(entireProcessTree: true);
        appium?.Dispose();
        return ValueTask.CompletedTask;
    }

    private IWebElement Element(string testId) =>
        driver?.FindElement(By.CssSelector($"[data-testid='{testId}']"))
        ?? throw new InvalidOperationException($"Android session {PlayerName} is not started.");

    private static int Port(string url) => new Uri(url).Port;

    private static async Task InstallAsync(string apk, string serial, CancellationToken cancellationToken)
    {
        ProcessStartInfo info = new("adb", $"-s {serial} install -r \"{apk}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not start adb install.");
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"adb install failed: {error}");
    }
}
