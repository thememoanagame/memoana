using System.Linq.Expressions;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Domain.Common;
using MemoAna.Backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MemoAna.Backend.Infrastructure.Common.Repository;

/// <summary>
/// Provides EF Core persistence operations for the relational model.
/// </summary>
/// <typeparam name="TEntity">The supported relational entity type.</typeparam>
public sealed class Repository<TEntity>(
    MemoAnaDbContext dbContext) : IRepository<TEntity>
    where TEntity : class, IRelationalEntityBase
{
    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        dbContext.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entity => entity.Id == id,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = dbContext.Set<TEntity>()
            .AsNoTracking();

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        _ = await dbContext.Set<TEntity>()
            .AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        bool exists = await dbContext.Set<TEntity>()
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == entity.Id,
                cancellationToken);

        if (!exists)
        {
            return false;
        }

        _ = dbContext.Set<TEntity>().Update(entity);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        TEntity? entity = await dbContext.Set<TEntity>()
            .FindAsync([id], cancellationToken);

        if (entity is null)
        {
            return false;
        }

        _ = dbContext.Set<TEntity>().Remove(entity);
        return true;
    }
}
