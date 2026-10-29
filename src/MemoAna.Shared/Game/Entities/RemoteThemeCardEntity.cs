using MemoAna.Common.Entities;

namespace MemoAna.Game.Entities;

public sealed class RemoteThemeCardEntity(string id) : EntityBase(id)
{
    // Caminho relativo da imagem, ex: "/themes/animais/gato.webp"
    public string CardPath { get; set; } = string.Empty;

    // Propriedades de Localização mapeadas do JSON
    public string NameEnUs { get; set; } = string.Empty;
    public string NamePtBr { get; set; } = string.Empty;

    // Chave Estrangeira e Navegação
    public string RemoteThemeIndexId { get; set; } = string.Empty;
    public RemoteThemeIndexEntity? RemoteThemeIndex { get; set; }
}
