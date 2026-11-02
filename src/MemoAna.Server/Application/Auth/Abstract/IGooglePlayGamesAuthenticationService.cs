using MemoAna.Auth.Responses;

namespace MemoAna.Auth.Abstract;

/// <summary>Google Play Games Authentication Service abstraction.</summary>
public interface IGooglePlayGamesAuthenticationService
{
    /// <summary>Authenticates a user via Google Play Games and issues JWT tokens.</summary>
    /// <param name="serverAuthCode">The server authentication code from the client.</param>
    /// <param name="redirectUri">The OAuth redirect URI, if any (or empty for standard Android server auth code).</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The token pair, or <see langword="null"/> when authentication fails.</returns>
    Task<TokenResponse?> AuthenticateAsync(string serverAuthCode, string redirectUri, CancellationToken cancellationToken);

    /// <summary>Links a Google Play Games identity to an existing authenticated user.</summary>
    /// <param name="userId">The identifier of the authenticated user.</param>
    /// <param name="serverAuthCode">The server authentication code from the client.</param>
    /// <param name="redirectUri">The OAuth redirect URI, if any.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The result of the linking operation.</returns>
    Task<IdentityResultResponse> LinkAsync(string userId, string serverAuthCode, string redirectUri, CancellationToken cancellationToken);
}
