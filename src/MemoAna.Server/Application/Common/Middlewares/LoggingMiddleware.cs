using Mediator;

namespace MemoAna.Server.Application.Common.Middlewares;

/// <summary>
/// Logs Mediator messages.
/// </summary>
/// <param name="logger">The logger to use.</param>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class LoggingMiddleware<TMessage,
    TResponse>(ILogger<TMessage> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    /// <summary>
    /// Logs the message before execution.
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
            logger.LogDebug("Executing message: {Name}", typeof(TMessage).Name);
            return await next(message, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while executing message '{Name}': {Message}", typeof(TMessage).Name, e.Message);
            throw;
        }
        finally
        {
            logger.LogDebug("Finished executing message: {Name}", typeof(TMessage).Name);
        }
    }
}
