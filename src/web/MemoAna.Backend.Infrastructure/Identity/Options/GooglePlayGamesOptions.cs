namespace MemoAna.Backend.Infrastructure.Identity.Options;

/// <summary>Options for Google Play Games authentication.</summary>
public sealed class GooglePlayGamesOptions
{
    /// <summary>Options section name. </summary>
    public const string SectionName = "GooglePlayGames";
    /// <summary> The Google Play Games Audience. </summary> 
    public string Audience { get; set; } = string.Empty;
    /// <summary> The Google Play Games Authority. </summary> 
    public string Authority { get; set; } = string.Empty;
    /// <summary>The Google Play Games Web Client ID.</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>The Google Play Games Web Client Secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary> The Google Play Games Valid Audience. </summary> 
    public string ValidAudience { get; set; } = string.Empty;
}
