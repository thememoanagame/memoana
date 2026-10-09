namespace MemoAna.Infrastructure.Api;

public interface IGameApiClient
{
    Task<IReadOnlyList<ThemeSummary>> ListThemesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DifficultyOption>> ListDifficultiesAsync(CancellationToken cancellationToken = default);
    Task<CreateRoomResponse> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);
    Task<GameState> GetStateAsync(string roomId, string accessToken, CancellationToken cancellationToken = default);
    Task<AssetManifest> GetManifestAsync(string roomId, string accessToken, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadAssetAsync(string roomId, string accessToken, string assetToken, CancellationToken cancellationToken = default);
}
