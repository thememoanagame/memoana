using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.UnitTests.Common.Mocks;

/// <summary>Provides an in-memory repository fake.</summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class FakeRepository<TEntity>
    : IRepository<TEntity>
    where TEntity : class, IRelationalEntityBase
{
    private readonly List<TEntity> _entities = [];

    /// <summary>Gets the stored entities.</summary>
    public IReadOnlyList<TEntity> Entities => _entities;

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _entities.FirstOrDefault(
                entity => entity.Id == id));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> ListAsync(
        System.Linq.Expressions.Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
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
    public Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        _entities.Add(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        int index = _entities.FindIndex(
            item => item.Id == entity.Id);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _entities[index] = entity;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        TEntity? entity = _entities.FirstOrDefault(
            item => item.Id == id);
        return Task.FromResult(
            entity is not null && _entities.Remove(entity));
    }
}
