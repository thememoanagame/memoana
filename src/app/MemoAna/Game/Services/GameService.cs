#pragma warning disable CA1416
using MemoAna.Game.Core;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Game.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;
using MemoAna.Game.Enums;
using MemoAna.Game.EventArgs;
using MemoAna.Game.Models;

namespace MemoAna.Game.Services;

public sealed class GameService : IGameService, IAsyncDisposable
{
    private readonly IThemeService themeService;
    private readonly IRepository<GameSettingsEntity> settingsRepository;
    private readonly IRepository<GameStatisticsEntity> statisticsRepository;
    private readonly IDispatcherTimer _gameTimer;
    private readonly IAIService aiService;
    private CancellationTokenSource? _gameCancellation;
    private MemoryCard? _firstSelectedCard;
    private MemoryCard? _secondSelectedCard;
    private int _firstPosition;
    private int _secondPosition;
    private bool _isProcessingTurn;
    private string _currentTheme = string.Empty;
    private GameDifficulty _currentDifficulty;
    public GameMode CurrentMode { get; private set; } = GameMode.TimeAttack;
    public bool IsHumanInteractionBlocked => CurrentMode == GameMode.IA && _isAiTurn;
    private bool _isAiTurn;
    private GameSettingsDto gameSettings = default!;
    private int _totalMoves;
    private int _successfulMoves;
    private int _mistakes;
    private int _currentStreak;
    private int _accumulatedScore;
    public int TotalMoves => _totalMoves;
    public int CurrentScore => _accumulatedScore;
    public ObservableCollection<KeyValuePair<int, MemoryCard>> CurrentCards { get; } = [];
    public TimeSpan RemainingTime { get; private set; }
    public bool IsGameActive { get; private set; }
    public event EventHandler<GameStatisticsEventArgs>? GameFinished;
    public event EventHandler<GameTickEventArgs>? TimerTick;
    public event EventHandler<GameCardFlippedEventArgs>? CardFlipped;
    public GameService(IThemeService themeService,
        IRepository<GameSettingsEntity> settingsRepository, 
        IRepository<GameStatisticsEntity> statisticsRepository,
        IDispatcher dispatcher,
        IAIService aiService)
    {
        this.themeService = themeService;
        this.settingsRepository = settingsRepository;
        this.statisticsRepository = statisticsRepository;
        this.aiService = aiService;

        _gameTimer = dispatcher.CreateTimer();
        _gameTimer.Interval = TimeSpan.FromSeconds(1);
        _gameTimer.Tick += OnTimerTick;
    }

    public async Task StartGameAsync(int difficulty, string theme, string mode = "1")
    {
        (int pairCount, int totalSeconds) = PresetGame(difficulty, theme, int.TryParse(mode, out var game_mode) ? game_mode : 1);

        gameSettings = GameSettingsDto.FromEntity((await settingsRepository.ListTrackedAsync(x => x != null, null!, CancellationToken.None))
                   .Single() ?? new());

        CardThemeDto cards = await themeService.GetThemeAsync(_currentTheme) ?? throw new KeyNotFoundException("Tema não disponível");

        var random = new Random();

        // .OrderBy(_ => random.Next()) ensures always get a random set of cards from manifest
        List<string> rawStrings = cards?.Base64Images.OrderBy(_ => random.Next()).Take(pairCount).ToList() 
            ?? throw new KeyNotFoundException("Imagens do tema não encontradas");

        var gameCards = new List<MemoryCard>();
        int idFactory = 0;
        string pairIdFactory = Guid.Empty.ToString();
        
        foreach (var base64Str in rawStrings)
        {
            if (string.IsNullOrEmpty(base64Str)) continue;
            pairIdFactory = Guid.CreateVersion7().ToString();
            
            gameCards.Add(new MemoryCard { Id = idFactory++, PairId = pairIdFactory, CardImage = base64Str });
            gameCards.Add(new MemoryCard { Id = idFactory++, PairId = pairIdFactory, CardImage = base64Str });
        }

        var shuffledCards = gameCards.OrderBy(_ => random.Next()).ToList();

        int i = 0;
        foreach (MemoryCard? card in shuffledCards)
            CurrentCards.Add(new KeyValuePair<int, MemoryCard>(i+=1, card));
        if (CurrentMode == GameMode.PVP)
            return;

        IsGameActive = true;
        RemainingTime = TimeSpan.FromSeconds(totalSeconds);
        if (CurrentMode == GameMode.IA)
            aiService.StartGame(_currentDifficulty, CurrentCards);
        else
            _gameTimer.Start();
    }

    private (int pairCount, int totalSeconds) PresetGame(int difficulty, string theme, int mode)
    {
        _gameCancellation?.Cancel();
        _gameCancellation?.Dispose();
        _gameCancellation = new CancellationTokenSource();
        aiService.Clear();
        _gameTimer.Stop();
        _firstSelectedCard = null;
        _secondSelectedCard = null;
        _isProcessingTurn = false;
        _isAiTurn = false;
        _currentTheme = theme;
        _currentDifficulty = (GameDifficulty)difficulty;
        CurrentMode = (GameMode)mode;
        _totalMoves = 0;
        _successfulMoves = 0;
        _mistakes = 0;
        _currentStreak = 0;
        _accumulatedScore = 0;

        CurrentCards.Clear();

        return _currentDifficulty switch
        {
            GameDifficulty.Easy => (6, 75),
            GameDifficulty.Medium => (10, 100),
            GameDifficulty.Hard => (15, 150),
            _ => (6, 75)
        };
    }

    public async Task FlipCardAsync(int position, MemoryCard selectedCard)
    {
        if (!IsGameActive || _isProcessingTurn || IsHumanInteractionBlocked ||
            selectedCard.IsFaceUp || selectedCard.IsMatched)
            return;

        selectedCard.IsFaceUp = true;

        if (_firstSelectedCard == null)
        {
            CardFlipped?.Invoke(this, new((position, selectedCard.CardImage)!));
            aiService.ObserveCard(position, selectedCard);
            _firstSelectedCard = selectedCard;
            _firstPosition = position;
            return;
        }

        CardFlipped?.Invoke(this, new((position, selectedCard.CardImage)!));
        aiService.ObserveCard(position, selectedCard);
        _secondSelectedCard = selectedCard;
        _secondPosition = position;
        _isProcessingTurn = true;
        _totalMoves++;

        if (_firstSelectedCard.PairId.Equals(_secondSelectedCard.PairId))
        {
            _firstSelectedCard.IsMatched = true;
            _secondSelectedCard.IsMatched = true;
            aiService.ObserveCard(_firstPosition, _firstSelectedCard);
            aiService.ObserveCard(_secondPosition, _secondSelectedCard);

            _successfulMoves++;
            _currentStreak++;

            _accumulatedScore = (_accumulatedScore + 1) * _currentStreak;

            ResetTurn();
            CheckWinCondition();
        }
        else
        {
            _mistakes++;
            _currentStreak = 0;

            await Task.Delay(gameSettings.Options.CardFlipDelayMs, _gameCancellation?.Token ?? CancellationToken.None);
            _firstSelectedCard?.IsFaceUp = false;
            _secondSelectedCard?.IsFaceUp = false;
            ResetTurn();
        }

        // Turns alternate consistently: every completed human turn gives the AI
        // one turn, regardless of whether the human found a pair.
        if (!_isAiTurn && CurrentMode == GameMode.IA && IsGameActive)
            await RunAiTurnAsync(_gameCancellation?.Token ?? CancellationToken.None);
    }
    private void ResetTurn()
    {
        _firstSelectedCard = null;
        _secondSelectedCard = null;
        _firstPosition = 0;
        _secondPosition = 0;
        _isProcessingTurn = false;
    }
     
    private void CheckWinCondition()
    {
        if (CurrentCards.All(c => c.Value.IsMatched))
            _ = EndGameAsync(won: true);
    }
    
    public void ForceStopTimer()
    {
        _gameCancellation?.Cancel();
        aiService.CancelPendingTurn();
        _isAiTurn = false;
        IsGameActive = false;
        _gameTimer?.Stop();
    }

    private void OnTimerTick(object? sender, System.EventArgs e)
    {
        if (!IsGameActive) return;

        RemainingTime = RemainingTime.Subtract(TimeSpan.FromSeconds(1));

        TimerTick?.Invoke(this, new GameTickEventArgs((int)RemainingTime.TotalSeconds));

        if (RemainingTime.TotalSeconds <= 0)
        {
            _ = EndGameAsync(won: false);
        }
    }

    private async Task RunAiTurnAsync(CancellationToken cancellationToken)
    {
        if (_isAiTurn || !IsGameActive)
            return;

        _isAiTurn = true;
        try
        {
            AITurn? turn = await aiService.GetNextTurnAsync(cancellationToken);
            if (turn is null || !IsGameActive)
                return;

            MemoryCard first = CurrentCards.Single(c => c.Key == turn.FirstPosition).Value;
            await FlipCardAsync(turn.FirstPosition, first);
            await Task.Delay(250, cancellationToken);
            MemoryCard second = CurrentCards.Single(c => c.Key == turn.SecondPosition).Value;
            await FlipCardAsync(turn.SecondPosition, second);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isAiTurn = false;
        }
    }

    private async Task EndGameAsync(bool won)
    {
        _gameCancellation?.Cancel();
        aiService.CancelPendingTurn();
        _isAiTurn = false;
        _gameTimer.Stop();
        IsGameActive = false;

        int finalScoreCalculated = _accumulatedScore;
        int remainingSeconds = CurrentMode == GameMode.TimeAttack
            ? (int)RemainingTime.TotalSeconds
            : 0;

        if (won && CurrentMode == GameMode.TimeAttack)
        {
            finalScoreCalculated += remainingSeconds * 25;
        }
        else
        {
            int unmatchedCardsCount = CurrentCards.Count(c => !c.Value.IsMatched);
            finalScoreCalculated -= unmatchedCardsCount * 50;
            if (finalScoreCalculated < 0)
                finalScoreCalculated = 0;
        }

        var stats = new GameStatisticsEntity
        {
            ThemeName = _currentTheme,
            Difficulty = _currentDifficulty,
            IsVictory = won,
            TotalMoves = _totalMoves,
            SuccessfulMoves = _successfulMoves,
            Mistakes = _mistakes,
            RemainingSeconds = remainingSeconds,
            FinalScore = finalScoreCalculated,
            PlayedAt = DateTime.UtcNow 
        };

        try
        {
            await statisticsRepository.AddAsync(stats, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Falha ao salvar Scoreboard: {ex.Message}");
        }
        finally
        {
            GameFinished?.Invoke(this, stats.ToEventArgs());
        }
    }

    public async ValueTask DisposeAsync()
    {
        ForceStopTimer();
        _gameTimer.Tick -= OnTimerTick;
        _gameCancellation?.Dispose();
        await Task.CompletedTask;
    }
}
#pragma warning restore CA1416
