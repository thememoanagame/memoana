namespace MemoAna.Game.Dtos;

public class RemoteThemeCardDto
{
    public string Id { get; set; } = string.Empty;
    public string CardPath { get; set; } = string.Empty;
    public LocalizedNameDto Name { get; set; } = new();
}