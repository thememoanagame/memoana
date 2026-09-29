using Microsoft.EntityFrameworkCore.ChangeTracking;
using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Entities;
using MemoAna.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace MemoAna.Common.Repositories;

/// <summary>
/// Provides EF Core persistence operations.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class Repository<TEntity> : IRepository<TEntity>
    where TEntity : EntityBase
{
    private readonly DbSet<TEntity> Set;
    private readonly IUnitOfWork uow;
    private readonly ILogger<Repository<TEntity>> logger;

    public Repository(GameDbContext dbContext, ILogger<Repository<TEntity>> logger, IUnitOfWork uow) =>
        (Set, this.logger, this.uow) = (dbContext.Set<TEntity>(), logger, uow);

    /// <inheritdoc />
    public async Task<TEntity?> GetByIdAsync(string id, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = Set.AsNoTracking();
        query = ApplyIncludes(query, includes);
        return await query.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TEntity?> GetTrackedByIdAsync(string id, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = Set;
        query = ApplyIncludes(query, includes);
        return await query.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = Set.AsNoTracking();
        query = ApplyIncludes(query, includes);
        return await query.FirstOrDefaultAsync(
            predicate,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken)
    {
        IQueryable<TEntity> query = Set.AsNoTracking();
        query = ApplyIncludes(query, includes);

        if (predicate is not null)
        {
            return await query.Where(predicate).ToListAsync(cancellationToken);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<TEntity?>> ListTrackedAsync(
        Expression<Func<TEntity, bool>>? predicate, 
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken) 
            => predicate is not null ?
                await ApplyIncludes(Set.Where(predicate), includes).ToListAsync(cancellationToken) :
                await ApplyIncludes(Set, includes).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken) 
        => Set.AsNoTracking().AnyAsync(predicate, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            await uow.BeginTransactionAsync(cancellationToken);

            EntityEntry<TEntity> entityEntry = await Set.AddAsync(entity, cancellationToken);

            await uow.CommitTransactionAsync(cancellationToken);
                
            await  uow.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error saving data : {Message}", e.Message);
            await uow.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            await uow.BeginTransactionAsync(cancellationToken);
            Set.Update(entity);
            await uow.CommitTransactionAsync(cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch(Exception e)
        {
            logger.LogError(e, "Error updating data : {Message}", e.Message);
            await uow.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(TEntity entity, CancellationToken cancellationToken)
    {
        try
        {
            await uow.BeginTransactionAsync(cancellationToken);
            Set.Remove(entity);
            await uow.CommitTransactionAsync(cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch(Exception e)
        {
            logger.LogError(e, "Error removing data : {Message}", e.Message);
            await uow.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken)
    {
        try
        {
            await uow.BeginTransactionAsync(cancellationToken);
            Set.RemoveRange(entities);
            await uow.CommitTransactionAsync(cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch(Exception e)
        {
            logger.LogError(e, "Error removing data range: {Message}", e.Message);
            await uow.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static IQueryable<TEntity> ApplyIncludes(IQueryable<TEntity> query,
        Expression<Func<TEntity, object?>>[] includes)
    {
        if (includes is null)
            return query;

        foreach (Expression<Func<TEntity, object?>> include in includes)
        {
            query = query.Include(include);
        }

        return query;
    }


}
