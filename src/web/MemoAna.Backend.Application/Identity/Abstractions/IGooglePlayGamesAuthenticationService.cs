using MemoAna.Backend.Application.Identity.Responses;

namespace MemoAna.Backend.Application.Identity.Abstractions;

/// <summary>Google Play Games Authentication Service abstraction.</summary>
public interface IGooglePlayGamesAuthenticationService
{
    /// <summary>Authenticates a user via Google Play Games and issues JWT tokens.</summary>
    /// <param name="serverAuthCode">The server authentication code from the client.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The token pair, or <see langword="null"/> when authentication fails.</returns>
    Task<TokenResponse?> AuthenticateAsync(string serverAuthCode, CancellationToken cancellationToken);
}
