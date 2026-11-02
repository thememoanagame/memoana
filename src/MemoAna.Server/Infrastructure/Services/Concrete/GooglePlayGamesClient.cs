using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Games.v1;
using Google.Apis.Services;
using MemoAna.Auth.Abstract;
using MemoAna.Server.Infrastructure.Auth.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoAna.Server.Infrastructure.Services.Concrete;

/// <summary>
/// Production implementation of <see cref="IGooglePlayGamesClient"/> using Google .NET SDKs.
/// </summary>
public class GooglePlayGamesClient(
    IOptions<GooglePlayGamesOptions> options,
    ILogger<GooglePlayGamesClient> logger) : IGooglePlayGamesClient
{
    private readonly GooglePlayGamesOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<GoogleTokenExchangeResult?> ExchangeCodeAsync(string serverAuthCode, string redirectUri, CancellationToken cancellationToken)
    {
        try
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _options.ClientId,
                    ClientSecret = _options.ClientSecret
                },
                Scopes = [
                    GamesService.Scope.Games,
                    GamesService.Scope.DriveAppdata,
                    "openid",
                    "profile",
                    "email"
                ]
            });

            // When exchanging a server authorization code obtained from an Android device via Play Games SDK,
            // no redirect URI was registered in the console for the client.
            // Google accepts redirect_uri="" (empty string) in this flow.
            string effectiveRedirectUri = !string.IsNullOrWhiteSpace(redirectUri)
                ? redirectUri
                : (!string.IsNullOrWhiteSpace(_options.RedirectUri) ? _options.RedirectUri : string.Empty);

            var tokenResponse = await flow.ExchangeCodeForTokenAsync(string.Empty, serverAuthCode, effectiveRedirectUri, cancellationToken);
            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                logger.LogWarning("Google Play Games token exchange returned an empty or invalid token.");
                return null;
            }

            return new GoogleTokenExchangeResult(
                tokenResponse.AccessToken,
                tokenResponse.IdToken,
                tokenResponse.RefreshToken,
                (int?)tokenResponse.ExpiresInSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException tre)
        {
            logger.LogWarning(tre, "Google token exchange failed: {Message}", tre.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to exchange server auth code with Google.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<GooglePlayerProfileResult?> GetPlayerProfileAsync(string accessToken, string? idToken, CancellationToken cancellationToken)
    {
        try
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            using var gamesService = new GamesService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "MemoAna"
            });

            var player = await gamesService.Players.Get("me").ExecuteAsync(cancellationToken);
            if (player == null || string.IsNullOrWhiteSpace(player.PlayerId))
            {
                logger.LogWarning("Could not retrieve player profile from Google Play Games.");
                return null;
            }

            string? email = null;
            bool emailVerified = false;
            string? givenName = player.Name?.GivenName;
            string? familyName = player.Name?.FamilyName;
            string? avatarUrl = player.AvatarImageUrl;

            if (!string.IsNullOrWhiteSpace(idToken))
            {
                try
                {
                    var validationSettings = new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = !string.IsNullOrWhiteSpace(_options.ClientId) ? [_options.ClientId] : null
                    };

                    var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);
                    if (payload != null)
                    {
                        email = payload.Email;
                        emailVerified = payload.EmailVerified;
                        givenName ??= payload.GivenName;
                        familyName ??= payload.FamilyName;
                        avatarUrl ??= payload.Picture;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to validate Google ID token payload.");
                }
            }

            return new GooglePlayerProfileResult(
                player.PlayerId,
                player.DisplayName,
                avatarUrl,
                email,
                emailVerified,
                givenName,
                familyName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve player profile from Google Play Games.");
            return null;
        }
    }
}

