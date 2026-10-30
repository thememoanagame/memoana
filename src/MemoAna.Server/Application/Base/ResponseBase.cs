using System;

namespace MemoAna.Server.Application.Base;

public record ResponseBase(string Message, bool Success)
{
    public static ResponseBase SuccessResponse(params string[] messages) => new (string.Join(", ", messages), true);
    public static ResponseBase FailureResponse(params string[] messages) => new (string.Join(", ", messages), false);
}

public sealed record ResponseBase<T>(T? Value, string Message, bool Success) 
    : ResponseBase(Message, Success)
{
    public static ResponseBase<T> SuccessResponse(T value, params string[] messages) => new (value, string.Join(", ", messages), true);
    public static new ResponseBase<T> FailureResponse(params string[] messages) => new (default, string.Join(", ", messages), false);
}
