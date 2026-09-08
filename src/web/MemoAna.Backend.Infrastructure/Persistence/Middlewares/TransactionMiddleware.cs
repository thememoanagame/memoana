using MemoAna.Backend.Application.Common.Abstractions;
using Mediator;

namespace MemoAna.Backend.Infrastructure.Persistence.Middlewares;

/// <summary>
/// Executes transactional Mediator requests atomically.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class TransactionMiddleware<TMessage,
    TResponse>(IUnitOfWork unitOfWork)
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
        if (message is not ITransactionalRequest)
        {
            return await next(message, cancellationToken);
        }

        await unitOfWork.BeginTransactionAsync(
            cancellationToken);

        try
        {
            TResponse response = await next(
                message,
                cancellationToken);

            _ = await unitOfWork.SaveChangesAsync(
                cancellationToken);
            await unitOfWork.CommitTransactionAsync(
                cancellationToken);

            return response;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);
            throw;
        }
    }
}
