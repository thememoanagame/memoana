using System;
using MemoAna.Game.Models;

namespace MemoAna.Server.Application.Relay.Responses;

public enum RelayResultStatus
{
    Success,
    BadRequest,
    NotFound,
    Unauthorized,
    Conflict,
    InternalError
}

public record RelayResult(bool Success, string Message, RelayResultStatus Status)
{
    public static RelayResult Ok(string message = "Success") => new(true, message, RelayResultStatus.Success);
    public static RelayResult BadRequest(string message) => new(false, message, RelayResultStatus.BadRequest);
    public static RelayResult NotFound(string message = "Room not found") => new(false, message, RelayResultStatus.NotFound);
    public static RelayResult Unauthorized(string message = "Invalid room password") => new(false, message, RelayResultStatus.Unauthorized);
    public static RelayResult Conflict(string message = "Room is full or unavailable") => new(false, message, RelayResultStatus.Conflict);
}

public sealed record RelayResult<T>(T? Value, bool Success, string Message, RelayResultStatus Status)
    : RelayResult(Success, Message, Status)
{
    public static RelayResult<T> Ok(T value, string message = "Success") => new(value, true, message, RelayResultStatus.Success);
    public static new RelayResult<T> BadRequest(string message) => new(default, false, message, RelayResultStatus.BadRequest);
    public static new RelayResult<T> NotFound(string message = "Room not found") => new(default, false, message, RelayResultStatus.NotFound);
    public static new RelayResult<T> Unauthorized(string message = "Invalid room password") => new(default, false, message, RelayResultStatus.Unauthorized);
    public static new RelayResult<T> Conflict(string message = "Room is full or unavailable") => new(default, false, message, RelayResultStatus.Conflict);
}
