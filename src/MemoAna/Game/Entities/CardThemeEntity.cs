using MemoAna.Common.Entities;

namespace MemoAna.Game.Entities;

public sealed class CardThemeEntity(string id) : EntityBase(id)
{
    public List<string> Base64Images { get; set; } = [];
    public string ManifestId { get; set; } = string.Empty;
    public CardThemeManifestEntity? Manifest { get; set; }
     
}