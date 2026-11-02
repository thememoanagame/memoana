using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MemoAna.Auth.Abstract;
using MemoAna.Auth.Responses;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoAna.Server.Tests;

public class GooglePlayGamesAuthenticationServiceTests
{
    private sealed class FakeGooglePlayGamesClient : IGooglePlayGamesClient
    {
        public GoogleTokenExchangeResult? TokenResult { get; set; }
        public Exception? ExchangeException { get; set; }
        public GooglePlayerProfileResult? ProfileResult { get; set; }
        public Exception? ProfileException { get; set; }

        public Task<GoogleTokenExchangeResult?> ExchangeCodeAsync(string serverAuthCode, string redirectUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ExchangeException != null)
            {
                throw ExchangeException;
            }
            return Task.FromResult(TokenResult);
        }

        public Task<GooglePlayerProfileResult?> GetPlayerProfileAsync(string accessToken, string? idToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ProfileException != null)
            {
                throw ProfileException;
            }
            return Task.FromResult(ProfileResult);
        }
    }

    private sealed class FakeIdentityService : IIdentityService
    {
        public string? LastProvider { get; private set; }
        public string? LastProviderKey { get; private set; }
        public ExternalUserProfile? LastProfile { get; private set; }
        public string? LastLinkedUserId { get; private set; }

        public Task<TokenResponse?> AuthenticateExternalAsync(string loginProvider, string providerKey, ExternalUserProfile? profile, CancellationToken cancellationToken)
        {
            LastProvider = loginProvider;
            LastProviderKey = providerKey;
            LastProfile = profile;
            return Task.FromResult<TokenResponse?>(new TokenResponse("Bearer", "access-token-123", 900, "refresh-token-123"));
        }

        public Task<TokenResponse?> AuthenticateExternalAsync(string loginProvider, string providerKey, CancellationToken cancellationToken) =>
            AuthenticateExternalAsync(loginProvider, providerKey, null, cancellationToken);

        public Task<IdentityResultResponse> LinkExternalAsync(string userId, string loginProvider, string providerKey, ExternalUserProfile? profile, CancellationToken cancellationToken)
        {
            LastLinkedUserId = userId;
            LastProvider = loginProvider;
            LastProviderKey = providerKey;
            LastProfile = profile;
            return Task.FromResult(IdentityResultResponse.Success());
        }

        public Task<IdentityResultResponse> RegisterAsync(string email, string password, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TokenResponse?> LoginAsync(string email, string password, string? twoFactorCode, string? twoFactorRecoveryCode, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> RevokeAsync(string accessToken, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ConfirmEmailAsync(string userId, string code, string? changedEmail, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityResultResponse> ResendConfirmationEmailAsync(string email, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityResultResponse> ForgotPasswordAsync(string email, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityResultResponse> ResetPasswordAsync(string email, string resetCode, string newPassword, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityInfoResponse?> GetInfoAsync(string userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IdentityResultResponse> UpdateInfoAsync(string userId, string? newEmail, string? newPassword, string oldPassword, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TwoFactorResponse?> ConfigureTwoFactorAsync(string userId, bool? enable, string? twoFactorCode, bool resetRecoveryCodes, bool resetSharedKey, bool forgetMachine, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    [Fact]
    public async Task AuthenticateAsync_ValidServerAuthCode_ReturnsTokensAndPopulatesProfile()
    {
        var client = new FakeGooglePlayGamesClient
        {
            TokenResult = new GoogleTokenExchangeResult("google-access-token", "google-id-token", "google-refresh-token", 3600),
            ProfileResult = new GooglePlayerProfileResult("player_12345", "SuperPlayer", "https://avatar.url/img.png", "player@example.com", true, "John", "Doe")
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync("valid-server-auth-code", "", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("access-token-123", result.AccessToken);
        Assert.Equal("GooglePlayGames", identityService.LastProvider);
        Assert.Equal("player_12345", identityService.LastProviderKey);
        Assert.NotNull(identityService.LastProfile);
        Assert.Equal("SuperPlayer", identityService.LastProfile.DisplayName);
        Assert.Equal("player@example.com", identityService.LastProfile.Email);
        Assert.True(identityService.LastProfile.EmailConfirmed);
        Assert.Equal("https://avatar.url/img.png", identityService.LastProfile.AvatarUrl);
        Assert.Equal("John", identityService.LastProfile.FirstName);
        Assert.Equal("Doe", identityService.LastProfile.SurName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthenticateAsync_InvalidCode_ReturnsNull(string? invalidCode)
    {
        var client = new FakeGooglePlayGamesClient();
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync(invalidCode!, "", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_ExchangeFails_ReturnsNull()
    {
        var client = new FakeGooglePlayGamesClient
        {
            TokenResult = null // exchange returns null
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync("failed-code", "", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_AccessTokenMissing_ReturnsNull()
    {
        var client = new FakeGooglePlayGamesClient
        {
            TokenResult = new GoogleTokenExchangeResult("", "id-token", null, 3600) // empty access token
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync("missing-token-code", "", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_ProfileUnavailable_ReturnsNull()
    {
        var client = new FakeGooglePlayGamesClient
        {
            TokenResult = new GoogleTokenExchangeResult("google-access-token"),
            ProfileResult = null // Profile could not be retrieved
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync("valid-code", "", CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthenticateAsync_PlayerWithoutIdentifier_ReturnsNull(string? emptyPlayerId)
    {
        var client = new FakeGooglePlayGamesClient
        {
            TokenResult = new GoogleTokenExchangeResult("google-access-token"),
            ProfileResult = new GooglePlayerProfileResult(emptyPlayerId!, "DisplayNameOnly")
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync("valid-code", "", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_PartialProfile_SucceedsWithAvailableFields()
    {
        var client = new FakeGooglePlayGamesClient
        {
            TokenResult = new GoogleTokenExchangeResult("google-access-token"),
            // Only player ID is provided, email and other fields are null
            ProfileResult = new GooglePlayerProfileResult("player_9999", null, null, null, false, null, null)
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync("partial-code", "", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("player_9999", identityService.LastProviderKey);
        Assert.NotNull(identityService.LastProfile);
        Assert.Null(identityService.LastProfile.Email);
        Assert.False(identityService.LastProfile.EmailConfirmed);
        Assert.Null(identityService.LastProfile.DisplayName);
    }

    [Fact]
    public async Task AuthenticateAsync_CommunicationFailure_ReturnsNullWithoutThrowing()
    {
        var client = new FakeGooglePlayGamesClient
        {
            ExchangeException = new System.Net.Http.HttpRequestException("Network timeout communicating with Google API")
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.AuthenticateAsync("network-fail-code", "", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_OperationCancelled_ThrowsOperationCanceledException()
    {
        var client = new FakeGooglePlayGamesClient();
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await service.AuthenticateAsync("any-code", "", cts.Token);
        });
    }

    [Fact]
    public async Task LinkAsync_ValidCode_CallsIdentityServiceAndSucceeds()
    {
        var client = new FakeGooglePlayGamesClient
        {
            TokenResult = new GoogleTokenExchangeResult("google-access-token"),
            ProfileResult = new GooglePlayerProfileResult("player_link_1", "LinkedGamer")
        };
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.LinkAsync("user_local_456", "valid-code", "", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("user_local_456", identityService.LastLinkedUserId);
        Assert.Equal("player_link_1", identityService.LastProviderKey);
        Assert.Equal("GooglePlayGames", identityService.LastProvider);
    }

    [Theory]
    [InlineData("", "valid-code")]
    [InlineData("valid-user", "")]
    public async Task LinkAsync_MissingUserIdOrCode_ReturnsFailure(string userId, string code)
    {
        var client = new FakeGooglePlayGamesClient();
        var identityService = new FakeIdentityService();
        var service = new GooglePlayGamesAuthenticationService(client, identityService, NullLogger<GooglePlayGamesAuthenticationService>.Instance);

        var result = await service.LinkAsync(userId, code, "", CancellationToken.None);

        Assert.False(result.Succeeded);
    }
}

