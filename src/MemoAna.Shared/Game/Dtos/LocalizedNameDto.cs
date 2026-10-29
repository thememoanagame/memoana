using System.Text.Json.Serialization;

namespace MemoAna.Game.Dtos;

public class LocalizedNameDto
{
    [JsonPropertyName("en_US")]
    public string EnUs { get; set; } = string.Empty;

    [JsonPropertyName("pt_BR")]
    public string PtBr { get; set; } = string.Empty;
}
