using FluentValidation;
using Mediator;

namespace MemoAna.Server.Application.Common.Middlewares;

/// <summary>
/// Validates Mediator messages with FluentValidation.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class ValidationMiddleware<TMessage,
    TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    /// <summary>
    /// Validates the message before execution.
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
        foreach (IValidator<TMessage> validator in validators)
        {
            await validator.ValidateAndThrowAsync(
                message,
                cancellationToken);
        }

        return await next(message, cancellationToken);
    }
}
