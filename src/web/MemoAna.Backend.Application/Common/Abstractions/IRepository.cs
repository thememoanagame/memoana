using System.Linq.Expressions;
using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.Application.Common.Abstractions;

/// <summary>
/// Defines persistence operations for supported relational entities.
/// </summary>
/// <typeparam name="TEntity">The supported relational entity type.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class, IRelationalEntityBase
{
    /// <summary>Gets an entity by its string identifier without tracking it.</summary>
    Task<TEntity?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>Lists supported entities, optionally filtered by a predicate.</summary>
    Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an entity to the current unit of work.</summary>
    Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>Stages an existing entity for update.</summary>
    Task<bool> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>Stages an existing entity for removal.</summary>
    Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default);
}
