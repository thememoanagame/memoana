using System.Linq.Expressions;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Domain.Common;
using MemoAna.Backend.Domain.Game;
using MemoAna.Backend.Infrastructure.Persistence;
using MongoDB.Driver;

namespace MemoAna.Backend.Infrastructure.Common.Repository;

/// <summary>
/// Provides MongoDB persistence operations for supported NoSQL documents.
/// </summary>
/// <typeparam name="TEntity">The supported NoSQL document type.</typeparam>
public sealed class NoRepository<TEntity>(
    MemoAnaMongoDbContext dbContext) : INoRepository<TEntity>
    where TEntity : class, INoSqlEntityBase
{
    /// <inheritdoc />
    public async Task<TEntity?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        IMongoCollection<TEntity> collection =
            dbContext.GetCollection<TEntity>();
        FilterDefinition<TEntity> filter =
            Builders<TEntity>.Filter.Eq(entity => entity.Id, id);

        return await collection.Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        IMongoCollection<TEntity> collection =
            dbContext.GetCollection<TEntity>();

        return await (predicate is null ?
            collection.Find(Builders<TEntity>.Filter.Empty) :
            collection.Find(predicate))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        await dbContext.GetCollection<TEntity>()
            .InsertOneAsync(entity, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        FilterDefinition<TEntity> filter =
            Builders<TEntity>.Filter.Eq(item => item.Id, entity.Id);
        ReplaceOneResult result = await dbContext.GetCollection<TEntity>()
            .ReplaceOneAsync(
                filter,
                entity,
                cancellationToken: cancellationToken);
        return result.MatchedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        FilterDefinition<TEntity> filter =
            Builders<TEntity>.Filter.Eq(entity => entity.Id, id);
        DeleteResult result = await dbContext.GetCollection<TEntity>()
            .DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount > 0;
    }

    private Task EnsureIndexesAsync(CancellationToken cancellationToken) =>
        typeof(TEntity) == typeof(CardThemeAssetEntity) ?
            dbContext.EnsureIndexesAsync(cancellationToken) :
            throw new InvalidOperationException(
                $"MongoDB persistence is not configured for {typeof(TEntity).FullName}.");
}
