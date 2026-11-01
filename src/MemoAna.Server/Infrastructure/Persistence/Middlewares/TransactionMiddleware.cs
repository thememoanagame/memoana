using Mediator;
using MemoAna.Common.Abstract.Repositories;

namespace MemoAna.Server.Infrastructure.Persistence.Middlewares;

/// <summary>
/// Executes transactional Mediator requests atomically.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class TransactionMiddleware<TMessage,
    TResponse>(IUnitOfWork unitOfWork, ILogger<TransactionMiddleware<TMessage,
    TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    /// <summary>
    /// Executes the request inside a database transaction
    /// when the message is transactional.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="next">The next pipeline stage.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The handler response.</returns>
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            if (message is not ITransactionalRequest)
            {
                logger.LogDebug("Message is not transactional. Skipping transaction.");
                return await next(message, cancellationToken);
            }

            logger.LogDebug("Message is transactional. Beginning transaction.");

            await unitOfWork.BeginTransactionAsync(
                cancellationToken);

            TResponse response = await next(
                message,
                cancellationToken);
            _ = await unitOfWork.SaveChangesAsync(
                cancellationToken);

            logger.LogDebug("Changes saved successfully. Committing transaction.");

            await unitOfWork.CommitTransactionAsync(
                cancellationToken);
            return response;
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while processing a transactional request: {Message}", e.Message);
            logger.LogDebug("Rolling back the transaction");
            await unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);
            throw;
        }
        finally
        {
            logger.LogDebug("Transaction middleware completed for message");
        }
    }
}
