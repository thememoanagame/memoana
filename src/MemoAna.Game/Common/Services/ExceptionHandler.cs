#pragma warning disable CA1416 // Validar a compatibilidade da plataforma

using MemoAna.Common.Abstract.ExceptionHandler;
using Microsoft.Extensions.Logging;

namespace MemoAna.Common.Services;

public sealed class ExceptionHandler(ILogger<ExceptionHandler> logger) : IExceptionHandler
{
    public async Task HandleAsync(Exception exception)
    {
        logger.LogError(exception, "Unhandled exception caught by ExceptionHandler");

        // garante execução na main thread (obrigatório pra exibir UI)
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var (title, message) = exception switch
            {
                KeyNotFoundException e => ("Error", e.Message),
                UnauthorizedAccessException => ("Error", "Please log in again."),
                _ => ("Error!", $"Unexpected error: {exception.Message}.")
            };

            var page = Microsoft.Maui.Controls.Application.Current?.Windows[0]?.Page;
            if (page is not null)
                await page.DisplayAlertAsync(title, message, "OK");
        });
    }
}

#pragma warning restore CA1416 // Validar a compatibilidade da plataforma