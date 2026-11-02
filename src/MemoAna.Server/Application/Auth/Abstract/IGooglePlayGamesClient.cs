namespace MemoAna.Auth.Abstract;

/// <summary>
/// Result of an OAuth token exchange with Google.
/// </summary>
/// <param name="AccessToken">Google OAuth access token.</param>
/// <param name="IdToken">Google OpenID Connect ID token, when available.</param>
/// <param name="RefreshToken">Google OAuth refresh token, when available.</param>
/// <param name="ExpiresInSeconds">Lifetime in seconds.</param>
public sealed record GoogleTokenExchangeResult(
    string AccessToken,
    string? IdToken = null,
    string? RefreshToken = null,
    int? ExpiresInSeconds = null);

/// <summary>
/// Verified player profile retrieved from Google Play Games Services.
/// </summary>
/// <param name="PlayerId">Stable player identifier from Google Play Games.</param>
/// <param name="DisplayName">Public player display name / gamer tag.</param>
/// <param name="AvatarImageUrl">Avatar or profile image URL.</param>
/// <param name="Email">Verified Google account email, when granted.</param>
/// <param name="EmailVerified">Whether the email was verified by Google.</param>
/// <param name="GivenName">Given / first name, when available.</param>
/// <param name="FamilyName">Family name / surname, when available.</param>
public sealed record GooglePlayerProfileResult(
    string PlayerId,
    string? DisplayName = null,
    string? AvatarImageUrl = null,
    string? Email = null,
    bool EmailVerified = false,
    string? GivenName = null,
    string? FamilyName = null);

/// <summary>
/// Abstraction for Google Play Games Services network communication.
/// Enables test isolation without real network calls.
/// </summary>
public interface IGooglePlayGamesClient
{
    /// <summary>Exchanges the server authorization code for Google OAuth tokens.</summary>
    Task<GoogleTokenExchangeResult?> ExchangeCodeAsync(string serverAuthCode, string redirectUri, CancellationToken cancellationToken);

    /// <summary>Retrieves the player profile from Google Play Games Services.</summary>
    Task<GooglePlayerProfileResult?> GetPlayerProfileAsync(string accessToken, string? idToken, CancellationToken cancellationToken);
}

