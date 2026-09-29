using MemoAna.Common.Enums;
using MemoAna.Game.Entities;
using MemoAna.Game.Models;

namespace MemoAna.Game.Dtos;

public sealed record GameSettingsDto(string Id, GameOptionsDto Options)
{
    public static GameSettingsDto FromEntity(GameSettingsEntity entity) =>
        new(entity is null ? 
            throw new ArgumentNullException($"Argument for {nameof(entity)} of type {typeof(GameSettingsEntity).Name} was null.") :
            entity.Id,
            GameOptionsDto.FromEntity(entity.Options));
}

public sealed class GameOptionsDto(int cardFlipDelayMs, bool confirmOnExit, bool cloudSaveEnabled, bool isHapticFeedbackEnabled, bool isMusicEnabled, bool isSfxEnabled, int language)
{
    public int CardFlipDelayMs { get; set; } = cardFlipDelayMs;
    public bool ConfirmOnExit { get; set; } = confirmOnExit;
    public bool CloudSaveEnabled { get; set; } = cloudSaveEnabled;
    public bool IsHapticFeedbackEnabled { get; set; } = isHapticFeedbackEnabled;
    public bool IsMusicEnabled { get; set; } = isMusicEnabled;
    public bool IsSfxEnabled { get; set; } = isSfxEnabled;
    public int Language { get; set; } = language;
    public static GameOptionsDto FromEntity(GameOptions options) =>
        new(options is null ? 
            throw new ArgumentNullException(nameof(options)) : 
            options.CardFlipDelayMs,
                options.ConfirmOnExit,
                options.CloudSaveEnabled,
                options.IsHapticFeedbackEnabled,
                options.IsMusicEnabled,
                options.IsSfxEnabled,
                (int)options.Language);

    public GameOptions ToEntity() => new()
    {
        CardFlipDelayMs = CardFlipDelayMs,
        CloudSaveEnabled = CloudSaveEnabled,
        ConfirmOnExit = ConfirmOnExit,
        IsHapticFeedbackEnabled = IsHapticFeedbackEnabled,
        IsMusicEnabled = IsMusicEnabled,
        IsSfxEnabled = IsSfxEnabled,
        Language = (Language)Language
    };

};
