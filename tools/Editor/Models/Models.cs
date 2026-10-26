namespace Editor.Models;

using System.Text.Json.Serialization;

public class LocalizedName
{
    [JsonPropertyName("en_US")]
    public string EnUs { get; set; } = string.Empty;
    
    [JsonPropertyName("pt_BR")]
    public string PtBr { get; set; } = string.Empty;
}

public class CardItem
{
    public string CardId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    
    // Caminho absoluto local para o MAUI renderizar a imagem no Editor
    [JsonIgnore] 
    public string LocalFullPath { get; set; } = string.Empty;
    
    public LocalizedName Name { get; set; } = new();
}

public class ThemeRegistry
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, string> Images { get; set; } = new(); // FileName -> Guid
}

public class RootRegistry
{
    public Dictionary<string, ThemeRegistry> Themes { get; set; } = new();
}