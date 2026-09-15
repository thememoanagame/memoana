using MemoAna.Backend.Application.Seed.Dtos;

namespace MemoAna.Backend.Application.Seed.Abstractions;

/// <summary>
/// Provides the explicit application seed operation and its current status.
/// </summary>
public interface IPostgresSeedService
{
    /// <summary>Gets the prerequisites required by the seed middleware.</summary>
    Task<SeedStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Ensures the fundamental application data exists.</summary>
    Task<SeedOperationResultDto> SeedAsync(
        CancellationToken cancellationToken = default);
}
