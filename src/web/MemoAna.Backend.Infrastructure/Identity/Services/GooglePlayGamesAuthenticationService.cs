using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Games.v1;
using Google.Apis.Services;
using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Infrastructure.Identity.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace MemoAna.Backend.Infrastructure.Identity.Services;

/// <summary>Implements Google Play Games authentication.</summary>
public sealed class GooglePlayGamesAuthenticationService(
    IOptions<GooglePlayGamesOptions> options,
    IIdentityService identityService,
    ILogger<GooglePlayGamesAuthenticationService> logger) : IGooglePlayGamesAuthenticationService
{
    private readonly GooglePlayGamesOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<Application.Identity.Responses.TokenResponse?> AuthenticateAsync(string serverAuthCode, string redirectUri, CancellationToken cancellationToken)
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
                Scopes = [GamesService.Scope.Games, 
                    GamesService.Scope.DriveAppdata,
                    "openid",
                    "profile",
                    "email"],
            });

            Google.Apis.Auth.OAuth2.Responses.TokenResponse? tokenResponse = null;
            try 
            {
                tokenResponse = await flow.ExchangeCodeForTokenAsync(string.Empty, serverAuthCode, redirectUri, cancellationToken);
            } 
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException tre)
            {
                logger.LogWarning(tre, "{Message}", tre.Message);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to exchange server auth code.");
                return null;
            }

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                logger.LogWarning("Google Play Games exchange returned an invalid token.");
                return null;
            }

            var credential = GoogleCredential.FromAccessToken(tokenResponse.AccessToken);
            using var gamesService = new GamesService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "MemoAna"
            });
            Google.Apis.Games.v1.Data.Player player = default!;
            try
            {
                player = await gamesService.Players.Get("me").ExecuteAsync(cancellationToken);
                if (player == null || string.IsNullOrEmpty(player.PlayerId))
                {
                    logger.LogWarning("Could not retrieve player ID from Google Play Games.");
                    return null;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The user doens't have a play games profile");
                return null;
            }

            return await identityService.AuthenticateExternalAsync(
                "GooglePlayGames",
                player.PlayerId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred during Google Play Games authentication.");
            return null;
        }
    }
}
