using System.Linq.Expressions;
using FluentValidation;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Game.Abstractions;
using MemoAna.Backend.Application.Game.Commands;
using MemoAna.Backend.Application.Game.Dtos;
using MemoAna.Backend.Application.Game.Handlers;
using MemoAna.Backend.Application.Game.Queries;
using MemoAna.Backend.Application.Game.Validators;
using MemoAna.Backend.Domain.Common;
using MemoAna.Backend.Domain.Game;
using MemoAna.Backend.Infrastructure.Game;

namespace MemoAna.Backend.UnitTests.Game;

public sealed class GameDataTests
{
    [Fact]
    public async Task Service_CreateAndUpdate_MapTheCompleteAggregateToDto()
    {
        FakeRelationalRepository<CardThemeEntity> themes = new();
        FakeRelationalRepository<CardThemeManifestEntity> manifests = new();
        FakeNoRepository assets = new();
        GameDataService service = new(themes, manifests, assets);

        GameDataDto created = await service.CreateAsync(
            "Ocean",
            true,
            "initial-image");
        GameDataDto? updated = await service.UpdateAsync(
            created.Id,
            "Forest",
            false,
            "updated-image");

        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Forest", updated.ThemeName);
        Assert.False(updated.IsDefault);
        Assert.Equal("updated-image", updated.Base64Image);
        Assert.Equal(created.PreviewAssetId, updated.PreviewAssetId);
    }

    [Fact]
    public async Task Service_GetAndList_ReturnOnlyCompleteAggregates()
    {
        FakeRelationalRepository<CardThemeEntity> themes = new();
        FakeRelationalRepository<CardThemeManifestEntity> manifests = new();
        FakeNoRepository assets = new();
        GameDataService service = new(themes, manifests, assets);

        GameDataDto complete = await service.CreateAsync(
            "Ocean",
            true,
            "image");
        CardThemeEntity incompleteTheme = new();
        incompleteTheme.ManifestId = new CardThemeManifestEntity().Id;
        await themes.AddAsync(incompleteTheme);

        Assert.NotNull(await service.GetByIdAsync(complete.Id));
        Assert.Null(await service.GetByIdAsync("missing"));
        IReadOnlyList<GameDataDto> result = await service.ListAsync();

        GameDataDto onlyItem = Assert.Single(result);
        Assert.Equal(complete.Id, onlyItem.Id);
    }

    [Fact]
    public async Task Service_Delete_RemovesAllAggregateDocuments()
    {
        FakeRelationalRepository<CardThemeEntity> themes = new();
        FakeRelationalRepository<CardThemeManifestEntity> manifests = new();
        FakeNoRepository assets = new();
        GameDataService service = new(themes, manifests, assets);
        GameDataDto created = await service.CreateAsync(
            "Ocean",
            false,
            "image");

        Assert.True(await service.DeleteAsync(created.Id));
        Assert.False(await service.DeleteAsync(created.Id));
        Assert.Empty(await service.ListAsync());
        Assert.Empty(manifests.Items);
        Assert.Empty(assets.Items);
    }

    [Fact]
    public async Task Handlers_EncapsulateServiceResultsAndMissingDataAsFailures()
    {
        FakeGameDataService service = new();
        GameDataHandlers handlers = new(service);
        CreateGameDataCommand create = new("Ocean", true, "image");

        ResponseAssert.Success(
            await handlers.Handle(create, CancellationToken.None));
        ResponseAssert.Success(
            await handlers.Handle(
                new GetGameDataListQuery(),
                CancellationToken.None));
        ResponseAssert.Success(
            await handlers.Handle(
                new GetGameDataQuery("existing"),
                CancellationToken.None));
        ResponseAssert.Success(
            await handlers.Handle(
                new UpdateGameDataCommand("existing", "Forest", false, "image"),
                CancellationToken.None));
        ResponseAssert.Success(
            await handlers.Handle(
                new DeleteGameDataCommand("existing"),
                CancellationToken.None));

        service.ReturnData = null;
        Assert.False((await handlers.Handle(
            new GetGameDataQuery("missing"),
            CancellationToken.None)).Succeeded);
        Assert.False((await handlers.Handle(
            new UpdateGameDataCommand("missing", "Forest", false, "image"),
            CancellationToken.None)).Succeeded);
        service.DeleteResult = false;
        Assert.False((await handlers.Handle(
            new DeleteGameDataCommand("missing"),
            CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Validators_RejectMissingRequiredGameData()
    {
        Assert.False((await new CreateGameDataCommandValidator()
            .ValidateAsync(new CreateGameDataCommand("", false, ""))).IsValid);
        Assert.False((await new GetGameDataQueryValidator()
            .ValidateAsync(new GetGameDataQuery(""))).IsValid);
        Assert.False((await new UpdateGameDataCommandValidator()
            .ValidateAsync(new UpdateGameDataCommand("", "", false, ""))).IsValid);
        Assert.False((await new DeleteGameDataCommandValidator()
            .ValidateAsync(new DeleteGameDataCommand(""))).IsValid);
    }

    private static class ResponseAssert
    {
        public static void Success<T>(
            MemoAna.Backend.Application.Common.Responses.Response<T> response) =>
            Assert.True(response.Succeeded);
    }

    private sealed class FakeGameDataService : IGameDataService
    {
        public GameDataDto? ReturnData { get; set; } =
            new("existing", "manifest", "Ocean", true, "asset", "image");

        public bool DeleteResult { get; set; } = true;

        public Task<GameDataDto> CreateAsync(
            string themeName,
            bool isDefault,
            string base64Image,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReturnData!);

        public Task<GameDataDto?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReturnData);

        public Task<IReadOnlyList<GameDataDto>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameDataDto>>(
                ReturnData is null ? [] : [ReturnData]);

        public Task<GameDataDto?> UpdateAsync(
            string id,
            string themeName,
            bool isDefault,
            string base64Image,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReturnData);

        public Task<bool> DeleteAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DeleteResult);
    }

    private sealed class FakeRelationalRepository<TEntity>
        : IRepository<TEntity>
        where TEntity : class, IRelationalEntityBase
    {
        public Dictionary<string, TEntity> Items { get; } = [];

        public Task<TEntity?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.GetValueOrDefault(id));

        public Task<IReadOnlyList<TEntity>> ListAsync(
            Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<TEntity> items = Items.Values;
            if (predicate is not null)
            {
                items = items.Where(predicate.Compile());
            }

            return Task.FromResult<IReadOnlyList<TEntity>>(items.ToArray());
        }

        public Task AddAsync(
            TEntity entity,
            CancellationToken cancellationToken = default)
        {
            Items[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(
            TEntity entity,
            CancellationToken cancellationToken = default)
        {
            bool exists = Items.ContainsKey(entity.Id);
            if (exists)
            {
                Items[entity.Id] = entity;
            }

            return Task.FromResult(exists);
        }

        public Task<bool> RemoveAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Remove(id));
    }

    private sealed class FakeNoRepository : INoRepository<CardThemeAssetEntity>
    {
        public Dictionary<string, CardThemeAssetEntity> Items { get; } = [];

        public Task<CardThemeAssetEntity?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.GetValueOrDefault(id));

        public Task<IReadOnlyList<CardThemeAssetEntity>> ListAsync(
            Expression<Func<CardThemeAssetEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<CardThemeAssetEntity> items = Items.Values;
            if (predicate is not null)
            {
                items = items.Where(predicate.Compile());
            }

            return Task.FromResult<IReadOnlyList<CardThemeAssetEntity>>(
                items.ToArray());
        }

        public Task AddAsync(
            CardThemeAssetEntity entity,
            CancellationToken cancellationToken = default)
        {
            Items[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(
            CardThemeAssetEntity entity,
            CancellationToken cancellationToken = default)
        {
            bool exists = Items.ContainsKey(entity.Id);
            if (exists)
            {
                Items[entity.Id] = entity;
            }

            return Task.FromResult(exists);
        }

        public Task<bool> RemoveAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Remove(id));
    }
}
