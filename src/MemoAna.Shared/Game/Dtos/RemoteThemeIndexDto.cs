namespace MemoAna.Game.Dtos;

public class RemoteThemeIndexDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CollectionPath { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Hash { get; set; } = string.Empty;
}
