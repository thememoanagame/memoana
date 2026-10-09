using MemoAna.Common.Abstract.ExceptionHandler;

namespace MemoAna.Common.Concrete.Services;

public sealed class ExceptionHandler(ILogger<ExceptionHandler> logger) : IExceptionHandler
{
    public async Task HandleAsync(Exception exception)
    {
        logger.LogError(exception, "Exceção não tratada capturada pelo GlobalExceptionHandler");

        // garante execução na main thread (obrigatório pra exibir UI)
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var (title, message) = exception switch
            {
                KeyNotFoundException e => ("Erro", e.Message),
                UnauthorizedAccessException => ("Erro", "Faça login novamente."),
                _ => ("Erro!", $"Ocorreu um erro inesperado: {exception.Message}.")
            };

            var page = Microsoft.Maui.Controls.Application.Current?.Windows[0]?.Page;
            if (page is not null)
                await page.DisplayAlertAsync(title, message, "OK");
        });
    }
}
