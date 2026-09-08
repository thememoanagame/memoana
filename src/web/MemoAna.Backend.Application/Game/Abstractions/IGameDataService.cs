using MemoAna.Backend.Application.Game.Dtos;

namespace MemoAna.Backend.Application.Game.Abstractions;

/// <summary>
/// Coordinates card theme metadata and asset persistence.
/// </summary>
public interface IGameDataService
{
    Task<GameDataDto> CreateAsync(
        string themeName,
        bool isDefault,
        string base64Image,
        CancellationToken cancellationToken = default);

    Task<GameDataDto?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameDataDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<GameDataDto?> UpdateAsync(
        string id,
        string themeName,
        bool isDefault,
        string base64Image,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default);
}
