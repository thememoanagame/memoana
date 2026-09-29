using MemoAna.Common.Entities;
using MemoAna.Game.Models;

namespace MemoAna.Game.Entities;

public class GameSettingsEntity : EntityBase
{
    public GameSettingsEntity() : base(Guid.CreateVersion7().ToString()) { }

    public GameSettingsEntity(string id) : base(id) { }

    public GameOptions Options { get; set; } = new();
}