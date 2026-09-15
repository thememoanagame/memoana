namespace MemoAna.Backend.Application.Seed;

/// <summary>Represents an expected failure while initializing application seed data.</summary>
public sealed class SeedException(
    string message,
    IReadOnlyCollection<string>? errors = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>Gets the actionable seed errors.</summary>
    public IReadOnlyCollection<string> Errors { get; } =
        errors is { Count: > 0 } ? errors : [message];
}
