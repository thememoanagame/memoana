using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Persistence;
using MemoAna.Common.Repositories;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;
using MemoAna.Game.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoAna.UnitTests.Game;

public sealed class AssetServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GameDbContext _dbContext;
    private readonly UnitOfWork _uow;

    public AssetServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new GameDbContext(options);
        _dbContext.Database.Migrate();
        _uow = new UnitOfWork(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SyncAndSeed_And_GetThemeAsync_Flow()
    {
        var remoteIndexRepo = new Repository<RemoteThemeIndexEntity>(_dbContext, NullLogger<Repository<RemoteThemeIndexEntity>>.Instance, _uow);
        var remoteCardRepo = new Repository<RemoteThemeCardEntity>(_dbContext, NullLogger<Repository<RemoteThemeCardEntity>>.Instance, _uow);
        var manifestRepo = new Repository<CardThemeManifestEntity>(_dbContext, NullLogger<Repository<CardThemeManifestEntity>>.Instance, _uow);
        var cardThemeRepo = new Repository<CardThemeEntity>(_dbContext, NullLogger<Repository<CardThemeEntity>>.Instance, _uow);

        var assetLogger = new ConsoleLogger<AssetService>();
        var httpClient = new HttpClient();
        var assetService = new AssetService(httpClient, assetLogger, remoteIndexRepo, remoteCardRepo, manifestRepo, cardThemeRepo);

        await assetService.SyncAndSeedAssetsAsync(TestContext.Current.CancellationToken);

        var manifests = await _dbContext.CardThemeManifests.ToListAsync();
        Console.WriteLine($"Manifests in DB count: {manifests.Count}");
        foreach (var m in manifests)
        {
            Console.WriteLine($"Manifest: {m.Id}, {m.ThemeName}");
        }

        var themeService = new ThemeService(manifestRepo);
        CardThemeDto theme = await themeService.GetThemeAsync("Cars");

        Assert.NotNull(theme);
        Assert.NotEmpty(theme.ImagesSrc);
        Assert.All(theme.ImagesSrc, src => Assert.StartsWith("https://", src));
    }

    [Fact]
    public async Task StartGameAsync_WithSyncedTheme_BuildsBoardAndActivates()
    {
        var remoteIndexRepo = new Repository<RemoteThemeIndexEntity>(_dbContext, NullLogger<Repository<RemoteThemeIndexEntity>>.Instance, _uow);
        var remoteCardRepo = new Repository<RemoteThemeCardEntity>(_dbContext, NullLogger<Repository<RemoteThemeCardEntity>>.Instance, _uow);
        var manifestRepo = new Repository<CardThemeManifestEntity>(_dbContext, NullLogger<Repository<CardThemeManifestEntity>>.Instance, _uow);
        var cardThemeRepo = new Repository<CardThemeEntity>(_dbContext, NullLogger<Repository<CardThemeEntity>>.Instance, _uow);
        var settingsRepo = new Repository<GameSettingsEntity>(_dbContext, NullLogger<Repository<GameSettingsEntity>>.Instance, _uow);
        var statisticsRepo = new Repository<GameStatisticsEntity>(_dbContext, NullLogger<Repository<GameStatisticsEntity>>.Instance, _uow);

        var assetLogger = new ConsoleLogger<AssetService>();
        var httpClient = new HttpClient();
        var assetService = new AssetService(httpClient, assetLogger, remoteIndexRepo, remoteCardRepo, manifestRepo, cardThemeRepo);
        await assetService.SyncAndSeedAssetsAsync(TestContext.Current.CancellationToken);

        var themeService = new ThemeService(manifestRepo);
        var dispatcher = new FakeDispatcher();
        var ai = new AIService(new FakeRandomSource());
        var gameService = new GameService(themeService, settingsRepo, statisticsRepo, dispatcher, ai);

        await gameService.StartGameAsync(0, "Cars", "1");

        Assert.True(gameService.IsGameActive);
        Assert.NotEmpty(gameService.CurrentCards);
        Assert.All(gameService.CurrentCards, c => Assert.StartsWith("https://", c.Value.CardImage));
    }

    private sealed class ConsoleLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
            if (exception != null) Console.WriteLine(exception.ToString());
        }
    }

    private sealed class FakeRandomSource : MemoAna.Game.Models.IRandomSource
    {
        public int Next(int maxExclusive) => 0;
        public double NextDouble() => 0.5;
    }

    private sealed class FakeDispatcher : Microsoft.Maui.Dispatching.IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public Microsoft.Maui.Dispatching.IDispatcherTimer CreateTimer() => new FakeTimer();
    }

    private sealed class FakeTimer : Microsoft.Maui.Dispatching.IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }

    [Fact]
    public async Task SyncWithMockHttp_PopulatesEntities_AndCanStartGameWithoutNetwork()
    {
        var remoteIndexRepo = new Repository<RemoteThemeIndexEntity>(_dbContext, NullLogger<Repository<RemoteThemeIndexEntity>>.Instance, _uow);
        var remoteCardRepo = new Repository<RemoteThemeCardEntity>(_dbContext, NullLogger<Repository<RemoteThemeCardEntity>>.Instance, _uow);
        var manifestRepo = new Repository<CardThemeManifestEntity>(_dbContext, NullLogger<Repository<CardThemeManifestEntity>>.Instance, _uow);
        var cardThemeRepo = new Repository<CardThemeEntity>(_dbContext, NullLogger<Repository<CardThemeEntity>>.Instance, _uow);
        var settingsRepo = new Repository<GameSettingsEntity>(_dbContext, NullLogger<Repository<GameSettingsEntity>>.Instance, _uow);
        var statisticsRepo = new Repository<GameStatisticsEntity>(_dbContext, NullLogger<Repository<GameStatisticsEntity>>.Instance, _uow);

        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Register("https://thememoanagame.github.io/memoana/index.json",
            "[{\"id\":\"mock-theme-1\",\"name\":\"MockTheme\",\"collectionPath\":\"/themes/mock/index.json\",\"thumbnail\":\"/themes/mock/thumbnail.webp\",\"count\":6,\"hash\":\"hash123\"}]");

        mockHandler.Register("https://thememoanagame.github.io/memoana/themes/mock/index.json",
            "[{\"id\":\"card-1\",\"cardPath\":\"/themes/mock/1.webp\",\"name\":{\"enUs\":\"One\",\"ptBr\":\"Um\"}}," +
            "{\"id\":\"card-2\",\"cardPath\":\"/themes/mock/2.webp\",\"name\":{\"enUs\":\"Two\",\"ptBr\":\"Dois\"}}," +
            "{\"id\":\"card-3\",\"cardPath\":\"/themes/mock/3.webp\",\"name\":{\"enUs\":\"Three\",\"ptBr\":\"Tres\"}}," +
            "{\"id\":\"card-4\",\"cardPath\":\"/themes/mock/4.webp\",\"name\":{\"enUs\":\"Four\",\"ptBr\":\"Quatro\"}}," +
            "{\"id\":\"card-5\",\"cardPath\":\"/themes/mock/5.webp\",\"name\":{\"enUs\":\"Five\",\"ptBr\":\"Cinco\"}}," +
            "{\"id\":\"card-6\",\"cardPath\":\"/themes/mock/6.webp\",\"name\":{\"enUs\":\"Six\",\"ptBr\":\"Seis\"}}]");

        var httpClient = new HttpClient(mockHandler);
        var assetService = new AssetService(httpClient, NullLogger<AssetService>.Instance, remoteIndexRepo, remoteCardRepo, manifestRepo, cardThemeRepo);

        await assetService.SyncAndSeedAssetsAsync(TestContext.Current.CancellationToken);

        var themeService = new ThemeService(manifestRepo);
        CardThemeDto theme = await themeService.GetThemeAsync("MockTheme");

        Assert.NotNull(theme);
        Assert.Equal(6, theme.ImagesSrc.Count);
        Assert.All(theme.ImagesSrc, src =>
        {
            Assert.StartsWith("https://thememoanagame.github.io/memoana/themes/mock/", src);
            Assert.DoesNotContain("base64", src, StringComparison.OrdinalIgnoreCase);
        });

        var dispatcher = new FakeDispatcher();
        var ai = new AIService(new FakeRandomSource());
        var gameService = new GameService(themeService, settingsRepo, statisticsRepo, dispatcher, ai);

        await gameService.StartGameAsync(0, "MockTheme", "0");

        Assert.True(gameService.IsGameActive);
        Assert.Equal(12, gameService.CurrentCards.Count);
        Assert.All(gameService.CurrentCards, card =>
        {
            Assert.NotNull(card.Value.CardImage);
            Assert.StartsWith("https://thememoanagame.github.io/memoana/themes/mock/", card.Value.CardImage);
            Assert.DoesNotContain("base64", card.Value.CardImage, StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string url, string jsonContent) => _responses[url] = jsonContent;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            if (_responses.TryGetValue(url, out string? json))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
