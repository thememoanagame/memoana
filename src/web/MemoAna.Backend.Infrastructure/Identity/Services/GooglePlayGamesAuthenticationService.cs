using System.Security.Cryptography;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Games.v1;
using Google.Apis.Services;
using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Identity.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
namespace MemoAna.Backend.Infrastructure.Identity.Services;

/// <summary>Implements Google Play Games authentication.</summary>
public sealed class GooglePlayGamesAuthenticationService(
    IOptions<GooglePlayGamesOptions> options,
    UserManager<User> userManager,
    IJwtTokenService tokenService,
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
            var gamesService = new GamesService(new BaseClientService.Initializer
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
            catch (Exception ex)
            {
                logger.LogError(ex, "The user doens't have a play games profile");
                return null;
            }

            string loginProvider = "GooglePlayGames";
            string providerKey = player.PlayerId;

            User? user = await userManager.FindByLoginAsync(loginProvider, providerKey);

            if (user == null)
            {
                user = new User
                {
                    UserName = $"gpg_{providerKey}",
                    Email = $"gpg_{providerKey}@playgames.local",
                    EmailConfirmed = true
                };

                // Generate a random secure password for the external user
                byte[] randomBytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(randomBytes);
                }
                string randomPassword = Convert.ToBase64String(randomBytes) + "A1a!";

                var createResult = await userManager.CreateAsync(user, randomPassword);
                if (!createResult.Succeeded)
                {
                    logger.LogError("Failed to create local user for Google Play Games identity. Errors: {Errors}", 
                        string.Join(", ", createResult.Errors.Select(e => e.Description)));
                    return null;
                }

                var addLoginResult = await userManager.AddLoginAsync(user, new UserLoginInfo(loginProvider, providerKey, "Google Play Games"));
                if (!addLoginResult.Succeeded)
                {
                    logger.LogError("Failed to add login info for Google Play Games identity.");
                    return null;
                }
            }

            var roles = await userManager.GetRolesAsync(user);
            return tokenService.CreateTokens(user.Id, user.Email ?? user.UserName ?? user.Id, roles, Enumerable.Empty<Claim>());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred during Google Play Games authentication.");
            return null;
        }
    }
}
