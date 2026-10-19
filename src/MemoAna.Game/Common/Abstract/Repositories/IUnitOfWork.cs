namespace MemoAna.Common.Abstract.Repositories;


/// <summary>
/// Coordinates persistence for a request.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists tracked changes.
    /// </summary>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// The number of changed entries.
    /// </returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts a database transaction.
    /// </summary>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task CommitTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rolls back the current transaction.
    /// </summary>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    Task RollbackTransactionAsync(CancellationToken cancellationToken);
}

