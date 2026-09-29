using CommunityToolkit.Mvvm.ComponentModel;

namespace MemoAna.Game.Core;

public partial class MemoryCard : ObservableObject
{
    public int Id { get; set; }
    public string PairId { get; set; } = string.Empty;
    public string? CardImage { get; set; }

    [ObservableProperty]
    public partial bool IsFaceUp { get; set; }

    [ObservableProperty]
    public partial bool IsMatched { get; set; }



}