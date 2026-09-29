using MemoAna.Game.Enums;

namespace MemoAna.Game.EventArgs;

public sealed class GameTurnChangedEventArgs(GameTurn currentTurn) : System.EventArgs
{
    public GameTurn CurrentTurn { get; } = currentTurn;
}
