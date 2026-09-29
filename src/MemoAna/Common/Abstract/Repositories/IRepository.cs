using MemoAna.Common.Entities;
using System.Linq.Expressions;

namespace MemoAna.Common.Abstract.Repositories;


/// <summary>
/// Defines persistence operations for entities.
/// </summary>
/// <typeparam name="TEntity">
/// The entity type.
/// </typeparam>
public interface IRepository<TEntity>
    where TEntity : EntityBase
{
    /// <summary>
    /// Gets an entity without tracking.
    /// </summary>
    /// <param name="id">
    /// The entity identifier.
    /// </param>
    /// <param name="includes">
    /// Navigation properties to include.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// The entity when found.
    /// </returns>
    Task<TEntity?> GetByIdAsync(string id, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a tracked entity.
    /// </summary>
    /// <param name="id">
    /// The entity identifier.
    /// </param>
    /// <param name="includes">
    /// Navigation properties to include.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// The entity when found.
    /// </returns>
    Task<TEntity?> GetTrackedByIdAsync(string id, Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken);
    
    Task<IReadOnlyCollection<TEntity?>> ListTrackedAsync(Expression<Func<TEntity, bool>>? predicate,  Expression<Func<TEntity, object?>>[] includes, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the first matching entity.
    /// </summary>
    /// <param name="predicate">
    /// The query predicate.
    /// </param>
    /// <param name="includes">
    /// Navigation properties to include.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// The first matching entity.
    /// </returns>
    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists entities matching a predicate.
    /// </summary>
    /// <param name="predicate">
    /// The optional query predicate.
    /// </param>
    /// <param name="includes">
    /// Navigation properties to include.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// The matching entities.
    /// </returns>
    Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate,
        Expression<Func<TEntity, object?>>[] includes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether an entity matches.
    /// </summary>
    /// <param name="predicate">
    /// The query predicate.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when found.
    /// </returns>
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken);

    /// <summary>
    /// Adds an entity to the unit of work.
    /// </summary>
    /// <param name="entity">
    /// The entity to add.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an entity in the unit of work.
    /// </summary>
    /// <param name="entity">
    /// The entity to update.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an entity from the unit of work.
    /// </summary>
    /// <param name="entity">
    /// The entity to remove.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task RemoveAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Removes entities from the unit of work.
    /// </summary>
    /// <param name="entities">
    /// The entities to remove.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken);
}
