using System.Linq.Expressions;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Domain.Common;
using MemoAna.Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MemoAna.Backend.Infrastructure.Common.Repository;

/// <summary>
/// Provides EF Core persistence operations.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class Repository<TEntity>(
    MemoAnaDbContext dbContext) : IRepository<TEntity>
    where TEntity : class, IEntityBase
{
    /// <inheritdoc />
    public async Task<TEntity?> GetByIdAsync(
        string id,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = dbContext
            .Set<TEntity>()
            .AsNoTracking();
        query = ApplyIncludes(query, includes);
        return await query.FirstOrDefaultAsync(
            entity => entity.Id == id,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TEntity?> GetTrackedByIdAsync(
        string id,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = dbContext.Set<TEntity>();
        query = ApplyIncludes(query, includes);
        return await query.FirstOrDefaultAsync(
            entity => entity.Id == id,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = dbContext
            .Set<TEntity>()
            .AsNoTracking();
        query = ApplyIncludes(query, includes);
        return await query.FirstOrDefaultAsync(
            predicate,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = dbContext
            .Set<TEntity>()
            .AsNoTracking();
        query = ApplyIncludes(query, includes);

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
    {
        return dbContext
            .Set<TEntity>()
            .AsNoTracking()
            .AnyAsync(predicate, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken)
    {
        _ = await dbContext
            .Set<TEntity>()
            .AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(TEntity entity)
    {
        _ = dbContext.Set<TEntity>().Update(entity);
    }

    /// <inheritdoc />
    public void Remove(TEntity entity)
    {
        _ = dbContext.Set<TEntity>().Remove(entity);
    }

    /// <inheritdoc />
    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        dbContext.Set<TEntity>().RemoveRange(entities);
    }

    private static IQueryable<TEntity> ApplyIncludes(
        IQueryable<TEntity> query,
        Expression<Func<TEntity, object?>>[] includes)
    {
        foreach (Expression<Func<TEntity, object?>> include
            in includes)
        {
            query = query.Include(include);
        }

        return query;
    }
}
