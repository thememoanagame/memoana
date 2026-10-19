using MemoAna.Game.Dtos;

namespace MemoAna.Common.Abstract.Services;

public interface ISettingsService
{
    Task<GameSettingsDto> LoadSettingsAsync();
    Task SaveSettingsAsync(GameSettingsDto dto = null!);
}
