namespace MemoAna.Backend.Application.Seed.Abstractions;

public interface ICardThemeAssetSeedService
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
