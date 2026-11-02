using MemoAna.Auth.Abstract;
using MemoAna.Auth.Responses;
using Microsoft.Extensions.Logging;

namespace MemoAna.Server.Infrastructure.Services.Concrete;

/// <summary>
/// Authenticates and links Google Play Games users by coordinating token exchange, profile retrieval, and local identity operations.
/// </summary>
public sealed class GooglePlayGamesAuthenticationService(
    IGooglePlayGamesClient googleClient,
    IIdentityService identityService,
    ILogger<GooglePlayGamesAuthenticationService> logger) : IGooglePlayGamesAuthenticationService
{
    /// <inheritdoc />
    public async Task<TokenResponse?> AuthenticateAsync(string serverAuthCode, string redirectUri, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverAuthCode))
        {
            logger.LogWarning("Server auth code cannot be null or empty.");
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            GoogleTokenExchangeResult? tokenResult = await googleClient.ExchangeCodeAsync(serverAuthCode, redirectUri, cancellationToken);
            if (tokenResult == null || string.IsNullOrWhiteSpace(tokenResult.AccessToken))
            {
                logger.LogWarning("Failed to exchange server auth code or missing access token.");
                return null;
            }

            GooglePlayerProfileResult? profile = await googleClient.GetPlayerProfileAsync(tokenResult.AccessToken, tokenResult.IdToken, cancellationToken);
            if (profile == null || string.IsNullOrWhiteSpace(profile.PlayerId))
            {
                logger.LogWarning("Google Play Games profile unavailable or missing PlayerId.");
                return null;
            }

            var externalProfile = new ExternalUserProfile(
                PlayerId: profile.PlayerId,
                DisplayName: profile.DisplayName,
                FirstName: profile.GivenName,
                SurName: profile.FamilyName,
                Email: profile.Email,
                EmailConfirmed: profile.EmailVerified,
                AvatarUrl: profile.AvatarImageUrl);

            return await identityService.AuthenticateExternalAsync(
                "GooglePlayGames",
                profile.PlayerId,
                externalProfile,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during Google Play Games authentication.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IdentityResultResponse> LinkAsync(string userId, string serverAuthCode, string redirectUri, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return IdentityResultResponse.Failure(["User identifier cannot be empty."]);
        }

        if (string.IsNullOrWhiteSpace(serverAuthCode))
        {
            return IdentityResultResponse.Failure(["Server authorization code cannot be empty."]);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            GoogleTokenExchangeResult? tokenResult = await googleClient.ExchangeCodeAsync(serverAuthCode, redirectUri, cancellationToken);
            if (tokenResult == null || string.IsNullOrWhiteSpace(tokenResult.AccessToken))
            {
                logger.LogWarning("Failed to exchange server auth code for linking.");
                return IdentityResultResponse.Failure(["Failed to validate external Google identity."]);
            }

            GooglePlayerProfileResult? profile = await googleClient.GetPlayerProfileAsync(tokenResult.AccessToken, tokenResult.IdToken, cancellationToken);
            if (profile == null || string.IsNullOrWhiteSpace(profile.PlayerId))
            {
                logger.LogWarning("Could not retrieve player profile from Google Play Games for linking.");
                return IdentityResultResponse.Failure(["Could not retrieve player profile from Google Play Games."]);
            }

            var externalProfile = new ExternalUserProfile(
                PlayerId: profile.PlayerId,
                DisplayName: profile.DisplayName,
                FirstName: profile.GivenName,
                SurName: profile.FamilyName,
                Email: profile.Email,
                EmailConfirmed: profile.EmailVerified,
                AvatarUrl: profile.AvatarImageUrl);

            return await identityService.LinkExternalAsync(
                userId,
                "GooglePlayGames",
                profile.PlayerId,
                externalProfile,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during Google Play Games account linking.");
            return IdentityResultResponse.Failure(["An unexpected error occurred during account linking."]);
        }
    }
}
