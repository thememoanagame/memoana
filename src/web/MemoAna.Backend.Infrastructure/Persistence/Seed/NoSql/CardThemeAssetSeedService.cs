using System.Reflection;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Domain.Game;

namespace MemoAna.Backend.Infrastructure.Persistence.Seed.NoSql;

/// <summary>
/// Builds one MongoDB asset document per seeded card theme.
/// </summary>
public sealed class CardThemeAssetSeedService(
    INoRepository<CardThemeAssetEntity> assetRepository)
{
    private static readonly SeedDefinition[] Definitions =
    [
        new("disney", "theme-disney"),
        new("marvel", "theme-marvel"),
        new("pokemon", "theme-pokemon"),
        new("píxar", "theme-cars")
    ];

    /// <summary>
    /// Reads embedded image resources and upserts one aggregate document per theme.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        Assembly assembly = typeof(CardThemeAssetSeedService).Assembly;
        foreach (SeedDefinition definition in Definitions)
        {
            List<string> images = [];
            string resourcePrefix = $"NoSql/{definition.ResourceFolder}/";

            foreach (string resourceName in assembly.GetManifestResourceNames()
                         .Where(name => name.Replace('\\', '/').StartsWith(
                             resourcePrefix,
                             StringComparison.OrdinalIgnoreCase))
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                await using Stream? resource = assembly.GetManifestResourceStream(resourceName);
                if (resource is null)
                {
                    throw new InvalidOperationException(
                        $"Embedded card theme resource '{resourceName}' could not be opened.");
                }

                using MemoryStream buffer = new();
                await resource.CopyToAsync(buffer, cancellationToken);
                images.Add(Convert.ToBase64String(buffer.ToArray()));
            }

            if (images.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No embedded images were found for '{definition.ResourceFolder}'.");
            }

            CardThemeAssetEntity asset = new(definition.CardThemeId)
            {
                CardThemeId = definition.CardThemeId,
                Base64Images = images
            };

            await assetRepository.UpsertAsync(
                asset,
                candidate => candidate.CardThemeId == definition.CardThemeId,
                cancellationToken);
        }
    }

    private sealed record SeedDefinition(
        string ResourceFolder,
        string CardThemeId);
}
