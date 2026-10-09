using MemoAna.Infrastructure.Api;

namespace MemoAna.Infrastructure.Session;

public sealed record GameSession(string RoomId, string PlayerId, string AccessToken, GameMode Mode, GameDifficulty Difficulty, string ThemeId);

public interface IGameSessionStore
{
    Task<GameSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(GameSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class SecureGameSessionStore : IGameSessionStore
{
    private const string Key = "memoana.game-session";
    public async Task<GameSession?> LoadAsync(CancellationToken ct = default)
    {
        var roomId = await SecureStorage.Default.GetAsync(Key + ".room");
        var playerId = await SecureStorage.Default.GetAsync(Key + ".player");
        var token = await SecureStorage.Default.GetAsync(Key + ".token");
        var mode = await SecureStorage.Default.GetAsync(Key + ".mode");
        var difficulty = await SecureStorage.Default.GetAsync(Key + ".difficulty");
        var theme = await SecureStorage.Default.GetAsync(Key + ".theme");
        return string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(token) ||
            !Enum.TryParse(mode, out GameMode parsedMode) || !Enum.TryParse(difficulty, out GameDifficulty parsedDifficulty)
            ? null : new(roomId, playerId, token, parsedMode, parsedDifficulty, theme ?? string.Empty);
    }

    public async Task SaveAsync(GameSession session, CancellationToken ct = default)
    {
        await SecureStorage.Default.SetAsync(Key + ".room", session.RoomId);
        await SecureStorage.Default.SetAsync(Key + ".player", session.PlayerId);
        await SecureStorage.Default.SetAsync(Key + ".token", session.AccessToken);
        await SecureStorage.Default.SetAsync(Key + ".mode", session.Mode.ToString());
        await SecureStorage.Default.SetAsync(Key + ".difficulty", session.Difficulty.ToString());
        await SecureStorage.Default.SetAsync(Key + ".theme", session.ThemeId);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        SecureStorage.Default.Remove(Key + ".room"); SecureStorage.Default.Remove(Key + ".player"); SecureStorage.Default.Remove(Key + ".token");
        SecureStorage.Default.Remove(Key + ".mode"); SecureStorage.Default.Remove(Key + ".difficulty"); SecureStorage.Default.Remove(Key + ".theme");
        return Task.CompletedTask;
    }
}
