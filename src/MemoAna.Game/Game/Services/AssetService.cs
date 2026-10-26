using System.Net.Http.Json;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;
using Microsoft.Extensions.Logging;

namespace MemoAna.Game.Services;

public class AssetService(
    HttpClient httpClient,
    ILogger<AssetService> logger,
    IRepository<RemoteThemeIndexEntity> remoteThemeIndex,
    IRepository<RemoteThemeCardEntity> remoteThemeCards,
    IRepository<CardThemeManifestEntity> themeManifest,
    IRepository<CardThemeEntity> themeCards) : Abstract.Services.IAssetService
{
    private const string BaseUrl = "https://thememoanagame.github.io/memoana";
    // Ensures only 1 thread per sync execution
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    
    /// <inheritdoc/>
    public async Task SyncAndSeedAssetsAsync(CancellationToken cancellationToken = default)
    {
        // Se já houver uma sincronização rodando, ele aguarda.
        // Se você usasse WaitAsync(0), poderia fazer ele abortar a segunda chamada instantaneamente.
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("Starting remote static assets sync...");

            try
            {
                List<RemoteThemeIndexDto>? remoteIndexes = await httpClient.GetFromJsonAsync<List<RemoteThemeIndexDto>>(
                    $"{BaseUrl}/index.json", cancellationToken);

                if (remoteIndexes is null || remoteIndexes.Count == 0)
                {
                    logger.LogWarning("No themes found at remote.");
                    return;
                }

                var localIndexes = await remoteThemeIndex.ListAsync(
                    predicate: null,
                    includes: [],
                    cancellationToken: cancellationToken);

                var remoteIds = remoteIndexes.Select(r => r.Id).ToHashSet();
                var themesToRemove = localIndexes.Where(l => !remoteIds.Contains(l.Id)).ToList();

                if (themesToRemove.Any(x => x is not null))
                {
                    await RemoveObsoleteThemesAsync(themesToRemove, cancellationToken);
                }

                var syncTasks = new List<Task>();

                foreach (var remoteTheme in remoteIndexes)
                {
                    var localTheme = localIndexes.FirstOrDefault(l => l.Id == remoteTheme.Id);

                    if (localTheme is null || localTheme.Hash != remoteTheme.Hash)
                    {
                        syncTasks.Add(ProcessThemeSyncAsync(remoteTheme, localTheme is not null, cancellationToken));
                    }
                }

                if (syncTasks.Any(x => x is not null))
                {
                    logger.LogInformation("Downloading and updating {Count} themes...", syncTasks.Count);
                    await Task.WhenAll(syncTasks);
                }
                else
                {
                    logger.LogInformation("All local themes are already updated.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failure when syncing remote assets.");
                throw;
            }
        }
        finally
        {
            // Libera o lock independentemente de sucesso ou erro
            _syncLock.Release();
        }
    }

    private async Task ProcessThemeSyncAsync(RemoteThemeIndexDto remoteThemeDto, bool isUpdate, CancellationToken cancellationToken)
    {
        try
        {
            var remoteCards = await httpClient.GetFromJsonAsync<List<RemoteThemeCardDto>>(
                $"{BaseUrl}{remoteThemeDto.CollectionPath}", cancellationToken);

            if (remoteCards is null) return;

            // Garantir que o CardThemeId se mantenha o mesmo durante atualizações
            string themeId = Guid.CreateVersion7().ToString();

            if (isUpdate)
            {
                var existingManifest = await themeManifest.FirstOrDefaultAsync(m => m.Id == remoteThemeDto.Id, [], cancellationToken);
                if (existingManifest is not null)
                {
                    themeId = existingManifest.CardThemeId;
                }
            }

            // 1. Prepara a árvore do Cache Remoto (Index + Cards)
            List<RemoteThemeCardEntity> remoteCardEntities = [.. remoteCards.Select(c => new RemoteThemeCardEntity(c.Id)
            {
                CardPath = c.CardPath,
                NameEnUs = c.Name.EnUs,
                NamePtBr = c.Name.PtBr,
                RemoteThemeIndexId = remoteThemeDto.Id
            })];

            var remoteIndexEntity = new RemoteThemeIndexEntity(remoteThemeDto.Id)
            {
                Name = remoteThemeDto.Name,
                CollectionPath = remoteThemeDto.CollectionPath,
                ThumbnailPath = remoteThemeDto.Thumbnail,
                Count = remoteThemeDto.Count,
                Hash = remoteThemeDto.Hash,
                Cards = remoteCardEntities // Vinculando as cartas ao Index
            };

            // 2. Prepara a árvore do Jogo (Manifest + Theme)
            var themeEntity = new CardThemeEntity(themeId)
            {
                ManifestId = remoteThemeDto.Id,
                ImagesSrc = [.. remoteCards.Select(c => $"{BaseUrl}{c.CardPath}")]
            };

            var manifestEntity = new CardThemeManifestEntity(remoteThemeDto.Id)
            {
                ThemeName = remoteThemeDto.Name,
                IsDefault = false,
                ThumbnailSrc = $"{BaseUrl}{remoteThemeDto.Thumbnail}", // Propriedade atualizada conforme seu log
                CardThemeId = themeEntity.Id,
                CardTheme = themeEntity // Vinculando o Theme ao Manifest
            };

            // 3. Persistência
            if (isUpdate)
            {
                // Para atualizar, atualizamos as raízes
                await remoteThemeIndex.UpdateAsync(remoteIndexEntity, cancellationToken);

                // Recria as cartas para evitar conflito de IDs
                var existingCards = await remoteThemeCards.ListAsync(c => c.RemoteThemeIndexId == remoteThemeDto.Id, [], cancellationToken);
                if (existingCards.Count > 0)
                {
                    await remoteThemeCards.RemoveRangeAsync(existingCards, cancellationToken);
                }
                foreach (var card in remoteCardEntities)
                {
                    await remoteThemeCards.AddAsync(card, cancellationToken);
                }

                await themeCards.UpdateAsync(themeEntity, cancellationToken);
                await themeManifest.UpdateAsync(manifestEntity, cancellationToken);
            }
            else
            {
                // Para inserção nova, passamos apenas as raízes.
                // O EF Core varre as propriedades Cards e CardTheme e insere 
                // os dependentes na ordem correta do banco!
                await remoteThemeIndex.AddAsync(remoteIndexEntity, cancellationToken);
                await themeManifest.AddAsync(manifestEntity, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao processar o tema {ThemeName}", remoteThemeDto.Name);
        }
    }

    private async Task RemoveObsoleteThemesAsync(List<RemoteThemeIndexEntity> themesToRemove, CancellationToken cancellationToken)
    {
        logger.LogInformation("Removendo {Count} temas que não existem mais no servidor estático...", themesToRemove.Count);

        // Remove o cache dos indexadores
        await remoteThemeIndex.RemoveRangeAsync(themesToRemove, cancellationToken);

        foreach (var theme in themesToRemove)
        {
            // Limpa as cartas cacheadas usando o repositório correto
            var existingCards = await remoteThemeCards.ListAsync(c => c.RemoteThemeIndexId == theme.Id, [], cancellationToken);
            if (existingCards.Count > 0)
            {
                await remoteThemeCards.RemoveRangeAsync(existingCards, cancellationToken);
            }

            // Limpa o manifesto e o tema original do jogo
            var manifest = await themeManifest.FirstOrDefaultAsync(m => m.Id == theme.Id, [], cancellationToken);
            if (manifest is not null)
            {
                var cardTheme = await themeCards.FirstOrDefaultAsync(t => t.Id == manifest.CardThemeId, [], cancellationToken);
                if (cardTheme is not null)
                {
                    await themeCards.RemoveAsync(cardTheme, cancellationToken);
                }
                
                await themeManifest.RemoveAsync(manifest, cancellationToken);
            }
        }
    }
}