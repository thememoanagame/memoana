namespace MemoAna.Game.Abstract.Services;

public interface IAssetService
{
    /// <summary>
    /// Validate remote assets comparing hashes with local database.
    /// Performs seed or partial/total updates only when needed,
    /// sync cache tables and native game models as well.
    /// </summary>
    Task SyncAndSeedAssetsAsync(CancellationToken cancellationToken = default);
}
