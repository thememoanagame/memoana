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
    private int _gameGeneration;
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private int _endedGeneration = -1;
    private string _currentTheme = string.Empty;
    private GameDifficulty _currentDifficulty;
    public GameMode CurrentMode { get; private set; } = GameMode.TimeAttack;
    public GameTurn CurrentTurn { get; private set; } = GameTurn.Player;
    public bool IsHumanInteractionBlocked => CurrentMode == GameMode.IA && CurrentTurn == GameTurn.AI;
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
    public event EventHandler<GameTurnChangedEventArgs>? TurnChanged;
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
        int gameGeneration = _gameGeneration;
        CancellationToken cancellationToken = _gameCancellation!.Token;

        gameSettings = GameSettingsDto.FromEntity((await settingsRepository.ListTrackedAsync(x => x != null, null!, cancellationToken))
                   .Single() ?? new());
        if (cancellationToken.IsCancellationRequested || gameGeneration != _gameGeneration)
            return;

        CardThemeDto cards = await themeService.GetThemeAsync(_currentTheme) ?? throw new KeyNotFoundException("Tema não disponível");
        if (cancellationToken.IsCancellationRequested || gameGeneration != _gameGeneration)
            return;

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

        if (cancellationToken.IsCancellationRequested || gameGeneration != _gameGeneration)
            return;
        IsGameActive = true;
        SetTurn(GameTurn.Player);
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
        _gameGeneration++;
        _endedGeneration = -1;
        CurrentTurn = GameTurn.Player;
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
        if (!IsGameActive || (CurrentMode == GameMode.IA && CurrentTurn != GameTurn.Player) || _isProcessingTurn ||
            selectedCard.IsFaceUp || selectedCard.IsMatched)
            return;

        await _turnGate.WaitAsync();
        try
        {
            if (!IsGameActive || (CurrentMode == GameMode.IA && CurrentTurn != GameTurn.Player) ||
                _isProcessingTurn || selectedCard.IsFaceUp || selectedCard.IsMatched)
                return;

            int gameGeneration = _gameGeneration;
            CancellationToken cancellationToken = _gameCancellation?.Token ?? CancellationToken.None;
            bool turnCompleted = await ExecuteCardFlipAsync(position, selectedCard, false, gameGeneration, cancellationToken);
            if (turnCompleted && CurrentMode == GameMode.IA &&
                IsGameActive && gameGeneration == _gameGeneration && !cancellationToken.IsCancellationRequested)
                await RunAiTurnAsync(gameGeneration, cancellationToken);
        }
        finally
        {
            _turnGate.Release();
        }
    }

    private async Task<bool> ExecuteCardFlipAsync(
        int position,
        MemoryCard selectedCard,
        bool isAiTurn,
        int gameGeneration,
        CancellationToken cancellationToken)
    {
        if (!IsGameActive || gameGeneration != _gameGeneration || cancellationToken.IsCancellationRequested ||
            (isAiTurn ? CurrentTurn != GameTurn.AI : CurrentTurn != GameTurn.Player) ||
            _isProcessingTurn || selectedCard.IsFaceUp || selectedCard.IsMatched)
            return false;

        selectedCard.IsFaceUp = true;
        NotifyCardFlipped(position, selectedCard);
        // State mutation and visual observability are separate phases. Give
        // the renderer a scheduling opportunity before AI memory advances.
        await Task.Yield();
        if (!IsGameActive || gameGeneration != _gameGeneration || cancellationToken.IsCancellationRequested)
            return false;

        aiService.ObserveCard(position, selectedCard);

        if (_firstSelectedCard == null)
        {
            _firstSelectedCard = selectedCard;
            _firstPosition = position;
            return false;
        }

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
            await CheckWinConditionAsync(gameGeneration);
        }
        else
        {
            _mistakes++;
            _currentStreak = 0;

            await Task.Delay(gameSettings.Options.CardFlipDelayMs, cancellationToken);
            _firstSelectedCard?.IsFaceUp = false;
            _secondSelectedCard?.IsFaceUp = false;
            NotifyCardFlipped(_firstPosition, _firstSelectedCard!);
            NotifyCardFlipped(_secondPosition, _secondSelectedCard!);
            await Task.Yield();
            ResetTurn();
        }

        return true;
    }
    private void ResetTurn()
    {
        _firstSelectedCard = null;
        _secondSelectedCard = null;
        _firstPosition = 0;
        _secondPosition = 0;
        _isProcessingTurn = false;
    }
     
    private async Task CheckWinConditionAsync(int gameGeneration)
    {
        if (CurrentCards.All(c => c.Value.IsMatched))
            await EndGameAsync(true, gameGeneration);
    }
    
    public void ForceStopTimer()
    {
        _gameCancellation?.Cancel();
        aiService.CancelPendingTurn();
        _gameGeneration++;
        SetTurn(GameTurn.Player);
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
            _ = EndGameAsync(won: false, _gameGeneration);
        }
    }

    private async Task RunAiTurnAsync(int gameGeneration, CancellationToken cancellationToken)
    {
        if (CurrentMode != GameMode.IA || !IsGameActive || gameGeneration != _gameGeneration ||
            cancellationToken.IsCancellationRequested || CurrentTurn != GameTurn.Player)
            return;

        SetTurn(GameTurn.AI);
        try
        {
            AITurn? turn = await aiService.GetNextTurnAsync(cancellationToken);
            if (turn is null || !IsGameActive || gameGeneration != _gameGeneration)
                return;

            MemoryCard first = CurrentCards.Single(c => c.Key == turn.FirstPosition).Value;
            await ExecuteCardFlipAsync(turn.FirstPosition, first, true, gameGeneration, cancellationToken);
            // The first reveal is a separate visual phase. Yield to the
            // renderer; CardFlipDelayMs belongs to the mismatch visible phase.
            await Task.Yield();
            MemoryCard second = CurrentCards.Single(c => c.Key == turn.SecondPosition).Value;
            await ExecuteCardFlipAsync(turn.SecondPosition, second, true, gameGeneration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (IsGameActive && gameGeneration == _gameGeneration && !cancellationToken.IsCancellationRequested)
                SetTurn(GameTurn.Player);
        }
    }

    private async Task EndGameAsync(bool won, int gameGeneration)
    {
        lock (_lifecycleLock)
        {
            if (gameGeneration != _gameGeneration || _endedGeneration == gameGeneration)
                return;

            _endedGeneration = gameGeneration;
        }

        _gameCancellation?.Cancel();
        aiService.CancelPendingTurn();
        SetTurn(GameTurn.Player);
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
            if (gameGeneration == _gameGeneration)
                GameFinished?.Invoke(this, stats.ToEventArgs());
        }
    }

    private void SetTurn(GameTurn turn)
    {
        if (CurrentTurn == turn)
            return;

        CurrentTurn = turn;
        TurnChanged?.Invoke(this, new(turn));
    }

    private void NotifyCardFlipped(int position, MemoryCard card) =>
        CardFlipped?.Invoke(this, new((position, card.CardImage)!));

    public async ValueTask DisposeAsync()
    {
        ForceStopTimer();
        _gameTimer.Tick -= OnTimerTick;
        _gameCancellation?.Dispose();
        await Task.CompletedTask;
    }
}
#pragma warning restore CA1416
