using MemoAna.Common.Entities;

namespace MemoAna.Game.Entities;

public sealed class RemoteThemeIndexEntity(string id) : EntityBase(id)
{
    /// <summary>Theme Name </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Theme Collection Uri Path </summary>
    public string CollectionPath { get; set; } = string.Empty;
    /// <summary>Theme ThumbnailSrc Uri Path </summary>
    public string ThumbnailPath { get; set; } = string.Empty;
    /// <summary>Theme Cards Count </summary>
    public int Count { get; set; }
    /// <summary>Theme Data Hash</summary>
    public string Hash { get; set; } = string.Empty;
    /// <summary>Cached cards relationship with this theme</summary> 
    public List<RemoteThemeCardEntity> Cards { get; set; } = [];
}
