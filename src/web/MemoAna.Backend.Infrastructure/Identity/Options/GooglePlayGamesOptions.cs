namespace MemoAna.Backend.Infrastructure.Identity.Options;

/// <summary>Options for Google Play Games authentication.</summary>
public sealed class GooglePlayGamesOptions
{
    /// <summary>The Google Play Games Web Client ID.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The Google Play Games Web Client Secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;
}
