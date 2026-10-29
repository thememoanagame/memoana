namespace MemoAna.Game.EventArgs;

public class GameCardFlippedEventArgs((int, string) card) : System.EventArgs
{
    public (int Position, string Card) MemoryCard { get; set; } = card;
}
