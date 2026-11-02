using System.Security.Claims;
using MemoAna.Auth.Responses;

namespace MemoAna.Auth.Abstract;

/// <summary>Creates, validates, and inspects application JWT tokens.</summary>
public interface IJwtTokenService
{
    /// <summary>Creates an access token and refresh token for a user.</summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="email">The user's email address.</param>
    /// <param name="roles">The roles to include in the access token.</param>
    /// <param name="claims">Additional claims to include in the access token.</param>
    /// <returns>The issued token pair.</returns>
    TokenResponse CreateTokens(string userId, string email, IEnumerable<string> roles, IEnumerable<Claim> claims);

    /// <summary>Validates a JWT and returns its claims principal when valid and not revoked.</summary>
    /// <param name="token">The JWT to validate.</param>
    /// <param name="validateLifetime">Indicates whether token lifetime should be validated.</param>
    /// <returns>The validated principal, or <see langword="null"/> when validation fails.</returns>
    ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true);

    /// <summary>Gets the JWT identifier without requiring the token to be valid.</summary>
    /// <param name="token">The encoded JWT.</param>
    /// <returns>The token identifier, or <see langword="null"/> when the token cannot be read.</returns>
    string? GetTokenId(string token);

    /// <summary>Gets the expiration timestamp encoded in a JWT.</summary>
    /// <param name="token">The encoded JWT.</param>
    /// <returns>The expiration timestamp, or <see langword="null"/> when it cannot be read.</returns>
    DateTime? GetExpiration(string token);
}
