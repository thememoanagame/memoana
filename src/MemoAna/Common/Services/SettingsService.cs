using MemoAna.Common.Abstract.Repositories;
using MemoAna.Common.Abstract.Services;
using MemoAna.Game.Dtos;
using MemoAna.Game.Entities;

namespace MemoAna.Common.Services;

public sealed class SettingsService(IRepository<GameSettingsEntity> repository) : ISettingsService
{
   
    public GameSettingsDto Settings { get; set; } = default!; 
    public async Task<GameSettingsDto> LoadSettingsAsync()
        => Settings =  GameSettingsDto.FromEntity(
                await repository.FirstOrDefaultAsync(
                    x => true, null!, CancellationToken.None) 
                    ?? new GameSettingsEntity());

    public async Task SaveSettingsAsync(GameSettingsDto dto = null!)
    {
        GameSettingsEntity gse = await repository.GetByIdAsync(dto is null ? Settings.Id : dto.Id, null!, CancellationToken.None) ?? new();
        gse.Options = Settings.Options.ToEntity()!;
        await repository.UpdateAsync(gse, CancellationToken.None);
    }
}
