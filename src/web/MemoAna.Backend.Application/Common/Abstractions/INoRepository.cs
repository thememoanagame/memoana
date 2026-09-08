using System.Linq.Expressions;
using MemoAna.Backend.Domain.Common;

namespace MemoAna.Backend.Application.Common.Abstractions;

/// <summary>
/// Defines persistence operations for supported NoSQL documents.
/// </summary>
/// <typeparam name="TEntity">The supported NoSQL entity type.</typeparam>
public interface INoRepository<TEntity>
    where TEntity : class, INoSqlEntityBase
{
    /// <summary>Gets a document by its stable string identifier.</summary>
    Task<TEntity?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>Lists supported documents, optionally filtered by a predicate.</summary>
    Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts a document.</summary>
    Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing document.</summary>
    Task<bool> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a document by its stable string identifier.</summary>
    Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default);
}
