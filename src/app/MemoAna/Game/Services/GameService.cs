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
    private bool _aiTurnInProgress;
    private bool _lastTurnMatched;
    private int _gameGeneration;
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private int _endedGeneration = -1;
    private string _currentTheme = string.Empty;
    private GameDifficulty _currentDifficulty;
    public GameMode CurrentMode { get; private set; } = GameMode.TimeAttack;
    public GameTurn CurrentTurn { get; private set; } = GameTurn.Player;
    public bool IsHumanInteractionBlocked => CurrentMode == GameMode.IA &&
        (CurrentTurn == GameTurn.AI /*|| _aiTurnInProgress*/);
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

    /// <summary>
    /// Creates a new generation, loads its settings and theme, and starts the
    /// selected mode on the player's turn. Cancelling and resetting happen in
    /// <see cref="PresetGame"/> before any asynchronous loading begins.
    /// </summary>
    /// <param name="difficulty">The configured difficulty value.</param>
    /// <param name="theme">The theme identifier to load.</param>
    /// <param name="mode">The persisted mode value: IA 0, TimeAttack 1, PVP 2.</param>
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
            CurrentCards.Add(new KeyValuePair<int, MemoryCard>(i += 1, card));

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

    /// <summary>
    /// Cancels the previous generation and resets all domain, statistics,
    /// timer, selection, turn, and AI state before a new game is loaded.
    /// </summary>
    /// <param name="difficulty">Difficulty selected by the player.</param>
    /// <param name="theme">Theme selected by the player.</param>
    /// <param name="mode">Numeric mode contract used by the UI.</param>
    /// <returns>The number of pairs and time limit for the new generation.</returns>
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
        _aiTurnInProgress = false;
        _lastTurnMatched = false;
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
        if (!IsGameActive || (CurrentMode == GameMode.IA && (CurrentTurn != GameTurn.Player /*|| _aiTurnInProgress*/)) || _isProcessingTurn ||
            selectedCard.IsFaceUp || selectedCard.IsMatched)
            return;

        await _turnGate.WaitAsync();
        try
        {
            if (!IsGameActive || (CurrentMode == GameMode.IA && (CurrentTurn != GameTurn.Player /*|| _aiTurnInProgress*/)) ||
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

    /// <summary>
    /// Applies one reveal to the authoritative board, notifies the UI, then
    /// records the card for AI memory only after the observability phase.
    /// Resolves a second card as match or mismatch and updates statistics.
    /// </summary>
    /// <param name="position">Position being revealed.</param>
    /// <param name="selectedCard">Card instance belonging to the current board.</param>
    /// <param name="isAiTurn">Whether the AI owns this reveal.</param>
    /// <param name="gameGeneration">Generation authorized to mutate state.</param>
    /// <param name="cancellationToken">Cancellation for lifecycle changes.</param>
    /// <returns><see langword="true"/> when a complete two-card turn was resolved.</returns>
    private async Task<bool> ExecuteCardFlipAsync(
        int position,
        MemoryCard selectedCard,
        bool isAiTurn,
        int gameGeneration,
        CancellationToken cancellationToken)
    {
        if (!IsGameActive || gameGeneration != _gameGeneration || cancellationToken.IsCancellationRequested ||
            (isAiTurn
                ? CurrentTurn != GameTurn.AI /*|| !_aiTurnInProgress*/
                : CurrentTurn != GameTurn.Player /*|| _aiTurnInProgress*/) ||
            _isProcessingTurn || selectedCard.IsFaceUp || selectedCard.IsMatched)
            return false;

        selectedCard.IsFaceUp = true;
        NotifyCardFlipped(position, selectedCard);
        // State mutation and visual observability are separate phases. Give
        // the renderer a scheduling opportunity before AI memory advances.
        await Task.Yield();
        if (!IsGameActive || gameGeneration != _gameGeneration || cancellationToken.IsCancellationRequested)
            return false;

        // The notification is the domain boundary for UI observability. A
        // short explicit asynchronous phase follows it; unlike Task.Yield,
        // this is a real delay and prevents the AI from immediately advancing
        // on the same scheduling turn as the reveal notification.
        await Task.Delay(50, cancellationToken);

        if (_firstSelectedCard == null)
        {
            _firstSelectedCard = selectedCard;
            _firstPosition = position;
            return false;
        }
        aiService.ObserveCard(position, selectedCard);

        _secondSelectedCard = selectedCard;
        _secondPosition = position;
        _isProcessingTurn = true;
        _lastTurnMatched = false;
        if (!isAiTurn)
            _totalMoves++;

        if (_firstSelectedCard.PairId.Equals(_secondSelectedCard.PairId))
        {
            _lastTurnMatched = true;
            _firstSelectedCard.IsMatched = true;
            _secondSelectedCard.IsMatched = true;
            if (!isAiTurn)
            {
                _successfulMoves++;
                _currentStreak++;
                _accumulatedScore = (_accumulatedScore + 1) * _currentStreak;
            }

            ResetTurn();
            await CheckWinConditionAsync(gameGeneration, isAiTurn ? GameTurn.AI : GameTurn.Player);
        }
        else
        {
            if (!isAiTurn)
            {
                _mistakes++;
                _currentStreak = 0;
            }

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
    /// <summary>
    /// Clears the two-card transaction and releases the service for the next
    /// turn after a match or mismatch has been fully resolved.
    /// </summary>
    private void ResetTurn()
    {
        _firstSelectedCard = null;
        _secondSelectedCard = null;
        _firstPosition = 0;
        _secondPosition = 0;
        _isProcessingTurn = false;
    }

    /// <summary>
    /// Ends the generation only when every card is matched, deriving victory
    /// from the turn that completed the final match rather than from the fact
    /// that the board is complete.
    /// </summary>
    /// <param name="gameGeneration">The generation that performed the match.</param>
    /// <param name="completingTurn">The player responsible for the final match.</param>
    private async Task CheckWinConditionAsync(int gameGeneration, GameTurn completingTurn)
    {
        if (CurrentCards.All(c => c.Value.IsMatched))
            await EndGameAsync(completingTurn == GameTurn.Player, gameGeneration);
    }

    /// <summary>
    /// Cancels all pending work, invalidates the generation, stops timing, and
    /// leaves the service inactive for navigation, restart, or disposal.
    /// </summary>
    public void ForceStopTimer()
    {
        _gameCancellation?.Cancel();
        aiService.CancelPendingTurn();
        _gameGeneration++;
        SetTurn(GameTurn.Player);
        IsGameActive = false;
        _gameTimer?.Stop();
    }

    /// <summary>Updates TimeAttack time and ends it as a player defeat at zero.</summary>
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

    /// <summary>
    /// Transitions to the AI, waits for its thinking strategy, reveals its two
    /// selected positions as separate visual phases, and returns control to
    /// the player only if the same generation is still active.
    /// </summary>
    /// <param name="gameGeneration">The generation authorized to mutate the board.</param>
    /// <param name="cancellationToken">Cancellation for restart, dispose, or end-game.</param>
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
            // This delay is deliberately inside the AI turn transaction. It must
            // never transfer ownership or enable human interaction.
            await Task.Delay(aiService.VisualRevealDelayMs, cancellationToken);

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

    /// <summary>
    /// Executes both reveals belonging to one AI turn while retaining AI
    /// ownership between them. The visual interval is part of this operation,
    /// so completion cannot be reported after only the first reveal.
    /// </summary>
    /// <param name="turn">The two positions selected by the AI strategy.</param>
    /// <param name="gameGeneration">The generation authorized to mutate state.</param>
    /// <param name="cancellationToken">Cancellation for restart, end, or disposal.</param>
    /// <returns><see langword="true"/> when both cards were processed, the pair matched, and the generation remains active.</returns>
    private async Task<bool> ExecuteAiTurnAsync(
        AITurn turn,
        int gameGeneration,
        CancellationToken cancellationToken)
    {
        MemoryCard first = CurrentCards.Single(c => c.Key == turn.FirstPosition).Value;
        if (!await ExecuteCardFlipAsync(turn.FirstPosition, first, true, gameGeneration, cancellationToken) ||
            !IsGameActive || gameGeneration != _gameGeneration)
            return false;

        // This delay is deliberately inside the AI turn transaction. It must
        // never transfer ownership or enable human interaction.
        await Task.Delay(aiService.VisualRevealDelayMs, cancellationToken);

        MemoryCard second = CurrentCards.Single(c => c.Key == turn.SecondPosition).Value;
        if (!await ExecuteCardFlipAsync(turn.SecondPosition, second, true, gameGeneration, cancellationToken) ||
            !IsGameActive || gameGeneration != _gameGeneration)
            return false;

        return _lastTurnMatched;
    }

    /// <summary>
    /// Atomically closes one generation, cancels pending work, persists the
    /// player-centric statistics, and emits <see cref="GameFinished"/> once.
    /// </summary>
    /// <param name="won">Whether the player, not merely any participant, won.</param>
    /// <param name="gameGeneration">The generation being ended.</param>
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

    /// <summary>
    /// Changes turn ownership and emits one transition event only when the
    /// value actually changes, preventing duplicate turn notifications.
    /// </summary>
    /// <param name="turn">The new owner of the turn.</param>
    private void SetTurn(GameTurn turn)
    {
        if (CurrentTurn == turn)
            return;

        CurrentTurn = turn;
        TurnChanged?.Invoke(this, new(turn));
    }

    /// <summary>
    /// Publishes the domain reveal/hide notification consumed by the UI before
    /// the AI is allowed to record a newly exposed card.
    /// </summary>
    /// <param name="position">The board position that changed.</param>
    /// <param name="card">The card whose image is now visible or hidden.</param>
    private void NotifyCardFlipped(int position, MemoryCard card) =>
        CardFlipped?.Invoke(this, new((position, card.CardImage)!));

    /// <summary>Stops lifecycle work and detaches the timer callback.</summary>
    public async ValueTask DisposeAsync()
    {
        ForceStopTimer();
        _gameTimer.Tick -= OnTimerTick;
        _gameCancellation?.Dispose();
        await Task.CompletedTask;
    }
}
#pragma warning restore CA1416
