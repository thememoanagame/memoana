using System.Linq.Expressions;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.UnitTests.Common.Mocks;

/// <summary>Provides an in-memory repository fake.</summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class FakeRepository<TEntity>
    : IRepository<TEntity>
    where TEntity : class, IEntityBase
{
    private readonly List<TEntity> _entities = [];

    /// <summary>Gets the stored entities.</summary>
    public IReadOnlyList<TEntity> Entities => _entities;

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(
        string id,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            _entities.FirstOrDefault(
                entity => entity.Id == id));
    }

    /// <inheritdoc />
    public Task<TEntity?> GetTrackedByIdAsync(
        string id,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        return GetByIdAsync(
            id,
            includes,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            _entities.AsQueryable()
                .FirstOrDefault(predicate));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken)
    {
        IEnumerable<TEntity> query = _entities;
        if (predicate is not null)
        {
            query = query.AsQueryable()
                .Where(predicate);
        }

        return Task.FromResult<IReadOnlyList<TEntity>>(
            [.. query]);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            _entities.AsQueryable()
                .Any(predicate));
    }

    /// <inheritdoc />
    public Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken)
    {
        _entities.Add(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Update(TEntity entity)
    {
        int index = _entities.FindIndex(
            item => item.Id == entity.Id);
        if (index >= 0)
        {
            _entities[index] = entity;
        }
    }

    /// <inheritdoc />
    public void Remove(TEntity entity)
    {
        _ = _entities.Remove(entity);
    }

    /// <inheritdoc />
    public void RemoveRange(
        IEnumerable<TEntity> entities)
    {
        foreach (TEntity entity in entities.ToArray())
        {
            Remove(entity);
        }
    }
}
