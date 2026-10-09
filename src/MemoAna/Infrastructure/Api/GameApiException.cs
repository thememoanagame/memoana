namespace MemoAna.Infrastructure.Api;

public sealed class GameApiException(string code, string message, int statusCode = 0) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
