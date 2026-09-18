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
    private readonly ILocalPVPService localPvpService;
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
    public bool IsHumanInteractionBlocked =>
        (CurrentMode == GameMode.IA || CurrentMode == GameMode.PVP) &&
        (CurrentTurn == GameTurn.AI || _pvpAwaitingResult);
    private GameSettingsDto gameSettings = default!;
    private int _totalMoves;
    private int _successfulMoves;
    private int _mistakes;
    private int _currentStreak;
    private int _accumulatedScore;
    private int _aiPairs;
    private int _aiMistakes;
    private int _aiStreak;
    private int _aiAccumulatedScore;
    private int _playerPairs;
    private int _pvpTurnId;
    private bool _pvpAwaitingResult;
    private MemoryCard? _pvpLocalFirstCard;
    private int _pvpLocalFirstPosition;
    private MemoryCard? _pvpRemoteFirstCard;
    private int _pvpRemoteFirstPosition;
    public int TotalMoves => _totalMoves;
    public int CurrentScore => _accumulatedScore;
    public int PlayerScore => _accumulatedScore;
    public int AIScore => _aiAccumulatedScore;
    public int PlayerSuccessfulMoves => _successfulMoves;
    public int AISuccessfulMoves => _aiPairs;
    public int PlayerMistakes => _mistakes;
    public int AIMistakes => _aiMistakes;
    private string LocalPvpPlayerId => localPvpService.IsHost ? "host" : "client";
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
        IAIService aiService,
        ILocalPVPService localPvpService)
    {
        this.themeService = themeService;
        this.settingsRepository = settingsRepository;
        this.statisticsRepository = statisticsRepository;
        this.aiService = aiService;
        this.localPvpService = localPvpService;

        _gameTimer = dispatcher.CreateTimer();
        _gameTimer.Interval = TimeSpan.FromSeconds(1);
        _gameTimer.Tick += OnTimerTick;
        localPvpService.MessageReceived += OnLocalPvpMessageReceived;
        localPvpService.ConnectionChanged += OnLocalPvpConnectionChanged;
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

        if (CurrentMode == GameMode.PVP && !localPvpService.IsHost)
        {
            LocalPvpMessageEventArgs message = await localPvpService.WaitForMessageAsync(LocalPvpMessageType.GameConfiguration, cancellationToken);
            LocalPvpGameConfiguration configuration = message.Deserialize<LocalPvpGameConfiguration>(LocalPvpJson.Options);
            _currentDifficulty = configuration.Difficulty;
            _currentTheme = configuration.ThemeName;
            foreach (LocalPvpBoardCard card in configuration.Board)
                CurrentCards.Add(new KeyValuePair<int, MemoryCard>(card.Position, card.ToMemoryCard()));
            SetTurn(configuration.InitialPlayerId == LocalPvpPlayerId ? GameTurn.Player : GameTurn.AI);
            await localPvpService.SendReadyAsync(cancellationToken);
        }
        else
        {
            CardThemeDto cards = await themeService.GetThemeAsync(_currentTheme) ?? throw new KeyNotFoundException("Tema não disponível");
            if (cancellationToken.IsCancellationRequested || gameGeneration != _gameGeneration)
                return;

            var random = new Random();
            List<string> rawStrings = cards.Base64Images.OrderBy(_ => random.Next()).Take(pairCount).ToList();
            var gameCards = new List<MemoryCard>();
            int idFactory = 0;

            foreach (string base64Str in rawStrings)
            {
                if (string.IsNullOrEmpty(base64Str)) continue;
                string pairId = Guid.CreateVersion7().ToString();
                gameCards.Add(new MemoryCard { Id = idFactory++, PairId = pairId, CardImage = base64Str });
                gameCards.Add(new MemoryCard { Id = idFactory++, PairId = pairId, CardImage = base64Str });
            }

            int position = 0;
            foreach (MemoryCard card in gameCards.OrderBy(_ => random.Next()))
                CurrentCards.Add(new KeyValuePair<int, MemoryCard>(++position, card));
        }

        if (cancellationToken.IsCancellationRequested || gameGeneration != _gameGeneration)
            return;
        IsGameActive = true;
        if (CurrentMode != GameMode.PVP)
            SetTurn(GameTurn.Player);
        RemainingTime = TimeSpan.FromSeconds(totalSeconds);
        if (CurrentMode == GameMode.IA)
            aiService.StartGame(_currentDifficulty, CurrentCards);
        else if (CurrentMode == GameMode.PVP && localPvpService.IsHost)
        {
            LocalPvpGameConfiguration configuration = new(
                localPvpService.RoomId!,
                _currentDifficulty,
                _currentTheme,
                CurrentCards.Select(card => new LocalPvpBoardCard(card.Key, card.Value.Id, card.Value.PairId, card.Value.CardImage)).ToList(),
                "host");
            await localPvpService.SendGameConfigurationAsync(configuration, cancellationToken);
            await localPvpService.WaitForMessageAsync(LocalPvpMessageType.GameReady, cancellationToken);
        }
        else
        {
            if (CurrentMode != GameMode.PVP)
                _gameTimer.Start();
        }
    }

    private async Task FlipPvpCardAsync(int position, MemoryCard selectedCard, CancellationToken cancellationToken)
    {
        selectedCard.IsFaceUp = true;
        NotifyCardFlipped(position, selectedCard);

        if (_pvpLocalFirstCard is null)
        {
            _pvpLocalFirstCard = selectedCard;
            _pvpLocalFirstPosition = position;
            _pvpTurnId++;
            await localPvpService.SendCardFlipAsync(new(position, _pvpTurnId, LocalPvpPlayerId), cancellationToken);
            return;
        }

        int firstPosition = _pvpLocalFirstPosition;
        _pvpAwaitingResult = true;
        await localPvpService.SendCardFlipAsync(new(position, _pvpTurnId, LocalPvpPlayerId), cancellationToken);
        if (localPvpService.IsHost)
            await ProcessPvpTurnAsync(firstPosition, position, true, cancellationToken);
    }

    private void OnLocalPvpMessageReceived(object? sender, LocalPvpMessageEventArgs message)
    {
        if (CurrentMode != GameMode.PVP || !IsGameActive)
            return;
        _ = HandleLocalPvpMessageAsync(message);
    }

    private void OnLocalPvpConnectionChanged(object? sender, LocalPvpConnectionEventArgs connection)
    {
        if (connection.Connected || CurrentMode != GameMode.PVP || !IsGameActive)
            return;

        IsGameActive = false;
        GameFinished?.Invoke(this, new(
            _currentTheme,
            _currentDifficulty,
            DateTime.UtcNow,
            false,
            0,
            _totalMoves,
            _successfulMoves,
            _mistakes,
            _accumulatedScore,
            _accumulatedScore,
            _aiAccumulatedScore,
            _aiAccumulatedScore,
            _aiPairs,
            _aiMistakes));
    }

    private async Task HandleLocalPvpMessageAsync(LocalPvpMessageEventArgs message)
    {
        await _turnGate.WaitAsync();
        try
        {
            switch (message.MessageType)
            {
                case LocalPvpMessageType.CardFlip:
                    await HandlePvpCardFlipAsync(message.Deserialize<LocalPvpCardFlip>(LocalPvpJson.Options));
                    break;
                case LocalPvpMessageType.TurnResult:
                case LocalPvpMessageType.GameFinished:
                    await HandlePvpTurnResultAsync(message.Deserialize<LocalPvpTurnResult>(LocalPvpJson.Options));
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Falha no PVP local: {ex.Message}");
            IsGameActive = false;
        }
        finally
        {
            _turnGate.Release();
        }
    }

    private async Task HandlePvpCardFlipAsync(LocalPvpCardFlip flip)
    {
        if (flip.PlayerId == LocalPvpPlayerId ||
            (localPvpService.IsHost && flip.PlayerId != "client") ||
            !CurrentCards.Any(card => card.Key == flip.Position))
            return;

        MemoryCard card = CurrentCards.Single(item => item.Key == flip.Position).Value;
        if (card.IsMatched || card.IsFaceUp)
            return;

        if (localPvpService.IsHost && CurrentTurn != GameTurn.AI)
            return;
        int expectedTurnId = _pvpRemoteFirstCard is null ? _pvpTurnId + 1 : _pvpTurnId;
        if (localPvpService.IsHost && flip.TurnId != expectedTurnId)
            return;

        card.IsFaceUp = true;
        NotifyCardFlipped(flip.Position, card);
        if (localPvpService.IsHost)
            _pvpTurnId = flip.TurnId;

        if (!localPvpService.IsHost)
            return;

        if (_pvpRemoteFirstCard is null)
        {
            _pvpRemoteFirstCard = card;
            _pvpRemoteFirstPosition = flip.Position;
            return;
        }

        int firstPosition = _pvpRemoteFirstPosition;
        _pvpRemoteFirstCard = null;
        await ProcessPvpTurnAsync(firstPosition, flip.Position, false, _gameCancellation?.Token ?? CancellationToken.None);
    }

    private async Task ProcessPvpTurnAsync(int firstPosition, int secondPosition, bool localPlayer, CancellationToken cancellationToken)
    {
        MemoryCard first = CurrentCards.Single(card => card.Key == firstPosition).Value;
        MemoryCard second = CurrentCards.Single(card => card.Key == secondPosition).Value;
        bool isMatch = first.PairId.Equals(second.PairId, StringComparison.Ordinal);

        if (localPlayer)
        {
            _totalMoves++;
            if (isMatch)
            {
                _successfulMoves++;
                _playerPairs++;
                _currentStreak++;
                _accumulatedScore = (_accumulatedScore + 1) * _currentStreak;
            }
            else
            {
                _mistakes++;
                _currentStreak = 0;
            }
        }
        else if (isMatch)
        {
            _aiPairs++;
            _aiStreak++;
            _aiAccumulatedScore = (_aiAccumulatedScore + 1) * _aiStreak;
        }
        else
        {
            _aiMistakes++;
            _aiStreak = 0;
        }

        if (isMatch)
        {
            first.IsMatched = true;
            second.IsMatched = true;
        }
        else
        {
            await Task.Delay(gameSettings.Options.CardFlipDelayMs, cancellationToken);
            first.IsFaceUp = false;
            second.IsFaceUp = false;
            NotifyCardFlipped(firstPosition, first);
            NotifyCardFlipped(secondPosition, second);
        }

        bool finished = CurrentCards.All(card => card.Value.IsMatched);
        string nextPlayerId = localPlayer ? "client" : "host";
        LocalPvpTurnResult result = new(
            _pvpTurnId,
            firstPosition,
            secondPosition,
            isMatch,
            localPlayer ? "host" : "client",
            finished ? LocalPvpPlayerId : nextPlayerId,
            CreatePvpStatistics(),
            finished);

        if (localPvpService.IsHost)
        {
            if (finished)
                await localPvpService.SendGameFinishedAsync(result, cancellationToken);
            else
                await localPvpService.SendTurnResultAsync(result, cancellationToken);
        }

        _pvpLocalFirstCard = null;
        _pvpAwaitingResult = false;
        if (finished)
        {
            await EndGameAsync(CalculatePvpGameResult(), _gameGeneration);
            return;
        }

        SetTurn(nextPlayerId == LocalPvpPlayerId ? GameTurn.Player : GameTurn.AI);
    }

    private async Task HandlePvpTurnResultAsync(LocalPvpTurnResult result)
    {
        if (localPvpService.IsHost)
            return;

        _pvpTurnId = Math.Max(_pvpTurnId, result.TurnId);
        MemoryCard first = CurrentCards.Single(card => card.Key == result.FirstPosition).Value;
        MemoryCard second = CurrentCards.Single(card => card.Key == result.SecondPosition).Value;
        first.IsFaceUp = true;
        second.IsFaceUp = true;
        if (result.IsMatch)
        {
            first.IsMatched = true;
            second.IsMatched = true;
        }
        else
        {
            await Task.Delay(gameSettings.Options.CardFlipDelayMs, _gameCancellation?.Token ?? CancellationToken.None);
            first.IsFaceUp = false;
            second.IsFaceUp = false;
            NotifyCardFlipped(result.FirstPosition, first);
            NotifyCardFlipped(result.SecondPosition, second);
        }

        ApplyPvpStatistics(result.Statistics);
        _pvpAwaitingResult = false;
        _pvpLocalFirstCard = null;
        if (result.IsGameFinished)
        {
            IsGameActive = false;
            GameFinished?.Invoke(this, CreatePvpStatisticsEventArgs(result.Statistics));
            return;
        }

        SetTurn(result.NextPlayerId == LocalPvpPlayerId ? GameTurn.Player : GameTurn.AI);
    }

    private LocalPvpStatistics CreatePvpStatistics() => new(
        _accumulatedScore,
        _aiAccumulatedScore,
        _successfulMoves,
        _aiPairs,
        _mistakes,
        _aiMistakes,
        _accumulatedScore,
        _aiAccumulatedScore,
        CalculatePvpGameResult() ? "host" : "client");

    private void ApplyPvpStatistics(LocalPvpStatistics statistics)
    {
        if (LocalPvpPlayerId == "host")
        {
            _accumulatedScore = statistics.HostScore;
            _aiAccumulatedScore = statistics.ClientScore;
            _successfulMoves = statistics.HostSuccessfulMoves;
            _aiPairs = statistics.ClientSuccessfulMoves;
            _mistakes = statistics.HostMistakes;
            _aiMistakes = statistics.ClientMistakes;
        }
        else
        {
            _accumulatedScore = statistics.ClientScore;
            _aiAccumulatedScore = statistics.HostScore;
            _successfulMoves = statistics.ClientSuccessfulMoves;
            _aiPairs = statistics.HostSuccessfulMoves;
            _mistakes = statistics.ClientMistakes;
            _aiMistakes = statistics.HostMistakes;
        }
    }

    private bool CalculatePvpGameResult() =>
        _accumulatedScore > _aiAccumulatedScore ||
        (_accumulatedScore == _aiAccumulatedScore && _playerPairs > _aiPairs);

    private GameStatisticsEventArgs CreatePvpStatisticsEventArgs(LocalPvpStatistics statistics)
    {
        ApplyPvpStatistics(statistics);
        bool playerWon = statistics.WinnerPlayerId == LocalPvpPlayerId;
        return new(_currentTheme, _currentDifficulty, DateTime.UtcNow, playerWon, 0, _totalMoves,
            _successfulMoves, _mistakes, _accumulatedScore, _accumulatedScore, _aiAccumulatedScore,
            _aiAccumulatedScore, _aiPairs, _aiMistakes);
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
        _playerPairs = 0;
        _aiPairs = 0;
        _aiMistakes = 0;
        _aiStreak = 0;
        _aiAccumulatedScore = 0;
        _pvpTurnId = 0;
        _pvpAwaitingResult = false;
        _pvpLocalFirstCard = null;
        _pvpRemoteFirstCard = null;
        _pvpLocalFirstPosition = 0;
        _pvpRemoteFirstPosition = 0;

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
        if (!IsGameActive || ((CurrentMode == GameMode.IA || CurrentMode == GameMode.PVP) && (CurrentTurn != GameTurn.Player || _pvpAwaitingResult)) || _isProcessingTurn ||
            selectedCard.IsFaceUp || selectedCard.IsMatched)
            return;

        await _turnGate.WaitAsync();
        try
        {
            if (!IsGameActive || ((CurrentMode == GameMode.IA || CurrentMode == GameMode.PVP) && (CurrentTurn != GameTurn.Player || _pvpAwaitingResult)) ||
                _isProcessingTurn || selectedCard.IsFaceUp || selectedCard.IsMatched)
                return;

            if (CurrentMode == GameMode.PVP)
            {
                await FlipPvpCardAsync(position, selectedCard, _gameCancellation?.Token ?? CancellationToken.None);
                return;
            }

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
                _playerPairs++;
                _currentStreak++;
                _accumulatedScore = (_accumulatedScore + 1) * _currentStreak;
            }
            else
            {
                _aiPairs++;
                _aiStreak++;
                _aiAccumulatedScore = (_aiAccumulatedScore + 1) * _aiStreak;
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
            else
            {
                _aiMistakes++;
                _aiStreak = 0;
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
    /// Ends the generation only when every card is matched. In IA mode, the
    /// final result is calculated from both participants' completed scores
    /// and pair counts rather than from the turn that completed the board.
    /// </summary>
    /// <param name="gameGeneration">The generation that performed the match.</param>
    /// <param name="completingTurn">The participant responsible for the final match.</param>
    private async Task CheckWinConditionAsync(int gameGeneration, GameTurn completingTurn)
    {
        if (CurrentCards.All(c => c.Value.IsMatched))
        {
            bool playerWon = CurrentMode == GameMode.IA
                ? CalculateAiGameResult()
                : completingTurn == GameTurn.Player;
            await EndGameAsync(playerWon, gameGeneration);
        }
    }

    /// <summary>
    /// Calculates the IA result after the board is complete. A player pair
    /// advantage always wins; when the IA has more pairs, the player may only
    /// overcome that deficit with a higher score. Equal pair counts use score.
    /// </summary>
    private bool CalculateAiGameResult()
    {
        if (_playerPairs > _aiPairs)
            return true;

        if (_aiPairs > _playerPairs)
            return _accumulatedScore > _aiAccumulatedScore;

        return _accumulatedScore > _aiAccumulatedScore;
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
        else if (CurrentMode != GameMode.IA)
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
                GameFinished?.Invoke(this, stats.ToEventArgs(
                    _accumulatedScore,
                    _aiAccumulatedScore,
                    _aiAccumulatedScore,
                    _aiPairs,
                    _aiMistakes));
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
        localPvpService.MessageReceived -= OnLocalPvpMessageReceived;
        localPvpService.ConnectionChanged -= OnLocalPvpConnectionChanged;
        await localPvpService.LeaveAsync();
        _gameCancellation?.Dispose();
        await Task.CompletedTask;
    }
}
