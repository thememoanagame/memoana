using MemoAna.Common.Enums;

namespace MemoAna.Game.Models;

public class GameOptions
{
    public bool IsMusicEnabled { get; set; } = true;
    public bool IsSfxEnabled { get; set; } = true;
    public Language Language { get; set; } = Language.pt_BR;
    public int CardFlipDelayMs { get; set; } = 750;
    public bool IsHapticFeedbackEnabled { get; set; } = true;
    public bool ConfirmOnExit { get; set; } = true;
    public bool CloudSaveEnabled { get; set; } = true;
}