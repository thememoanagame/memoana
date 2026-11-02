using System;
using System.Data.Common;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MemoAna.Auth.Abstract;
using MemoAna.Auth.Responses;
using MemoAna.Server.Application.Auth.Abstract;
using MemoAna.Server.Infrastructure.Auth.Models;
using MemoAna.Server.Infrastructure.Auth.Options;
using MemoAna.Server.Infrastructure.Persistence;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoAna.Server.Tests;

public class IdentityServiceTests : IDisposable
{
    private readonly DbConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly ServerDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRevokedTokenStore _revokedTokenStore;
    private readonly TestEmailSender _emailSender;

    private sealed class TestEmailSender : IIdentityEmailSender
    {
        public string? LastEmail { get; private set; }
        public string? LastLink { get; private set; }

        public Task SendConfirmationAsync(string email, string confirmationLink, CancellationToken cancellationToken)
        {
            LastEmail = email;
            LastLink = confirmationLink;
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken)
        {
            LastEmail = email;
            LastLink = resetLink;
            return Task.CompletedTask;
        }
    }

    public IdentityServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ServerDbContext>(options => options.UseSqlite(_connection));

        services.AddIdentity<User, Role>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = false;
        })
        .AddRoles<Role>()
        .AddEntityFrameworkStores<ServerDbContext>()
        .AddDefaultTokenProviders();

        services.AddHttpContextAccessor();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddAuthentication();

        var jwtOptions = Options.Create(new JwtOptions
        {
            Key = "TestSecretKeyForMemoAnaUnitTestingMustBeLongEnough123!",
            Issuer = "MemoAnaTests",
            Audience = "MemoAnaTests",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = TimeSpan.FromDays(7)
        });
        services.AddSingleton(jwtOptions);

        _revokedTokenStore = new RevokedTokenStore();
        services.AddSingleton<IRevokedTokenStore>(_revokedTokenStore);
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        _emailSender = new TestEmailSender();
        services.AddSingleton<IIdentityEmailSender>(_emailSender);

        services.AddScoped<IIdentityService, IdentityService>();

        _serviceProvider = services.BuildServiceProvider();

        _dbContext = _serviceProvider.GetRequiredService<ServerDbContext>();
        _dbContext.Database.EnsureCreated();

        _identityService = _serviceProvider.GetRequiredService<IIdentityService>();
        _userManager = _serviceProvider.GetRequiredService<UserManager<User>>();
        _signInManager = _serviceProvider.GetRequiredService<SignInManager<User>>();
        _jwtTokenService = _serviceProvider.GetRequiredService<IJwtTokenService>();

        var httpContextAccessor = _serviceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = new DefaultHttpContext { RequestServices = _serviceProvider };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task RegisterAsync_ValidCredentials_CreatesUserAndSendsConfirmation()
    {
        var result = await _identityService.RegisterAsync("player1@example.com", "Password123!", CancellationToken.None);

        Assert.True(result.Succeeded);
        var user = await _userManager.FindByEmailAsync("player1@example.com");
        Assert.NotNull(user);
        Assert.Equal("player1@example.com", user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("player1@example.com", _emailSender.LastEmail);
        Assert.Contains("/confirmEmail", _emailSender.LastLink);
    }

    [Fact]
    public async Task RegisterAsync_InvalidPassword_ReturnsFailure()
    {
        // Password too short / missing requirements
        var result = await _identityService.RegisterAsync("badpass@example.com", "123", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        await _identityService.RegisterAsync("loginuser@example.com", "Password123!", CancellationToken.None);

        var tokens = await _identityService.LoginAsync("loginuser@example.com", "Password123!", null, null, CancellationToken.None);

        Assert.NotNull(tokens);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsNull()
    {
        await _identityService.RegisterAsync("wrongpass@example.com", "Password123!", CancellationToken.None);

        var tokens = await _identityService.LoginAsync("wrongpass@example.com", "WrongPassword123!", null, null, CancellationToken.None);

        Assert.Null(tokens);
    }

    [Fact]
    public async Task LoginAsync_NonExistentEmail_ReturnsNull()
    {
        var tokens = await _identityService.LoginAsync("ghost@example.com", "Password123!", null, null, CancellationToken.None);

        Assert.Null(tokens);
    }

    [Fact]
    public async Task LoginAsync_WithTwoFactorEnabled_ValidatesCode()
    {
        await _identityService.RegisterAsync("twofa@example.com", "Password123!", CancellationToken.None);
        var user = await _userManager.FindByEmailAsync("twofa@example.com");
        Assert.NotNull(user);

        // Generate authenticator key and enable 2FA
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
        }
        await _userManager.SetTwoFactorEnabledAsync(user, true);

        // Login without 2FA code should fail
        var failedLogin = await _identityService.LoginAsync("twofa@example.com", "Password123!", null, null, CancellationToken.None);
        Assert.Null(failedLogin);

        // Login with invalid 2FA code should fail
        var invalidCodeLogin = await _identityService.LoginAsync("twofa@example.com", "Password123!", "999999", null, CancellationToken.None);
        Assert.Null(invalidCodeLogin);

        // Login with recovery code should succeed
        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 1);
        string recoveryCode = recoveryCodes!.First();

        var recoveryLogin = await _identityService.LoginAsync("twofa@example.com", "Password123!", null, recoveryCode, CancellationToken.None);
        Assert.NotNull(recoveryLogin);
    }

    [Fact]
    public async Task RefreshAsync_ValidRefreshToken_ReturnsNewTokensAndRevokesOld()
    {
        await _identityService.RegisterAsync("refresh@example.com", "Password123!", CancellationToken.None);
        var initialTokens = await _identityService.LoginAsync("refresh@example.com", "Password123!", null, null, CancellationToken.None);
        Assert.NotNull(initialTokens);

        var newTokens = await _identityService.RefreshAsync(initialTokens.RefreshToken, CancellationToken.None);

        Assert.NotNull(newTokens);
        Assert.NotEqual(initialTokens.AccessToken, newTokens.AccessToken);

        // Reusing the same refresh token must fail because it was revoked
        var reuseAttempt = await _identityService.RefreshAsync(initialTokens.RefreshToken, CancellationToken.None);
        Assert.Null(reuseAttempt);
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ReturnsNull()
    {
        var result = await _identityService.RefreshAsync("completely-invalid-jwt-token", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAsync_ValidAccessToken_RevokesTokenSuccessfully()
    {
        await _identityService.RegisterAsync("revoke@example.com", "Password123!", CancellationToken.None);
        var tokens = await _identityService.LoginAsync("revoke@example.com", "Password123!", null, null, CancellationToken.None);
        Assert.NotNull(tokens);

        bool revoked = await _identityService.RevokeAsync(tokens.AccessToken, CancellationToken.None);
        Assert.True(revoked);

        // Token should now be reported as revoked
        string? tokenId = _jwtTokenService.GetTokenId(tokens.AccessToken);
        Assert.NotNull(tokenId);
        Assert.True(_revokedTokenStore.IsRevoked(tokenId));
    }

    [Fact]
    public async Task AuthenticateExternalAsync_NewUser_CreatesUserWithProfileAndNoSyntheticEmail()
    {
        var profile = new ExternalUserProfile(
            PlayerId: "gpg_player_001",
            DisplayName: "CoolGamer",
            FirstName: "Gamer",
            SurName: "One",
            Email: "realgamer@gmail.com",
            EmailConfirmed: true,
            AvatarUrl: "https://avatar.com/p1.png");

        var tokens = await _identityService.AuthenticateExternalAsync("GooglePlayGames", "gpg_player_001", profile, CancellationToken.None);

        Assert.NotNull(tokens);
        var user = await _userManager.FindByLoginAsync("GooglePlayGames", "gpg_player_001");
        Assert.NotNull(user);
        Assert.Equal("gpg_player_001", user.PlayerId);
        Assert.Equal("CoolGamer", user.DisplayName);
        Assert.Equal("realgamer@gmail.com", user.Email);
        Assert.True(user.EmailConfirmed);
        Assert.Equal("https://avatar.com/p1.png", user.AvatarUrl);
        Assert.False(user.Email?.EndsWith("@external.local"));
    }

    [Fact]
    public async Task AuthenticateExternalAsync_WhenNoEmailProvided_UserHasNullEmailWithoutSyntheticFallback()
    {
        var profile = new ExternalUserProfile(
            PlayerId: "gpg_no_email_002",
            DisplayName: "SecretPlayer",
            Email: null,
            EmailConfirmed: false);

        var tokens = await _identityService.AuthenticateExternalAsync("GooglePlayGames", "gpg_no_email_002", profile, CancellationToken.None);

        Assert.NotNull(tokens);
        var user = await _userManager.FindByLoginAsync("GooglePlayGames", "gpg_no_email_002");
        Assert.NotNull(user);
        Assert.Null(user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("SecretPlayer", user.DisplayName);
    }

    [Fact]
    public async Task AuthenticateExternalAsync_ExistingExternalUser_LogsInAndUpdateMissingProfileFields()
    {
        // First authentication with minimal profile
        var profile1 = new ExternalUserProfile(PlayerId: "gpg_player_003", DisplayName: "OldName");
        await _identityService.AuthenticateExternalAsync("GooglePlayGames", "gpg_player_003", profile1, CancellationToken.None);

        // Second authentication with updated avatar
        var profile2 = new ExternalUserProfile(PlayerId: "gpg_player_003", DisplayName: "OldName", AvatarUrl: "https://newavatar.com/img.png");
        var tokens = await _identityService.AuthenticateExternalAsync("GooglePlayGames", "gpg_player_003", profile2, CancellationToken.None);

        Assert.NotNull(tokens);
        var user = await _userManager.FindByLoginAsync("GooglePlayGames", "gpg_player_003");
        Assert.NotNull(user);
        Assert.Equal("https://newavatar.com/img.png", user.AvatarUrl);
    }

    [Fact]
    public async Task LinkExternalAsync_Success_LinksExternalIdentityToExistingTraditionalUser()
    {
        await _identityService.RegisterAsync("traditional@example.com", "Password123!", CancellationToken.None);
        var user = await _userManager.FindByEmailAsync("traditional@example.com");
        Assert.NotNull(user);

        var profile = new ExternalUserProfile(PlayerId: "gpg_linked_100", DisplayName: "LinkedGamer");

        var linkResult = await _identityService.LinkExternalAsync(user.Id, "GooglePlayGames", "gpg_linked_100", profile, CancellationToken.None);

        Assert.True(linkResult.Succeeded);

        // User should now be found by GooglePlayGames external login
        var resolvedUser = await _userManager.FindByLoginAsync("GooglePlayGames", "gpg_linked_100");
        Assert.NotNull(resolvedUser);
        Assert.Equal(user.Id, resolvedUser.Id);
        Assert.Equal("LinkedGamer", resolvedUser.DisplayName);

        // Subsequently, logging in via GooglePlayGames external login returns valid tokens for this user
        var tokens = await _identityService.AuthenticateExternalAsync("GooglePlayGames", "gpg_linked_100", CancellationToken.None);
        Assert.NotNull(tokens);
    }

    [Fact]
    public async Task LinkExternalAsync_ExternalIdentityAlreadyLinkedToAnotherUser_ReturnsFailure()
    {
        // User A connects to Google Play Games
        await _identityService.RegisterAsync("usera@example.com", "Password123!", CancellationToken.None);
        var userA = await _userManager.FindByEmailAsync("usera@example.com");
        Assert.NotNull(userA);
        await _identityService.LinkExternalAsync(userA.Id, "GooglePlayGames", "shared_gpg_key", null, CancellationToken.None);

        // User B tries to link the SAME Google Play Games key
        await _identityService.RegisterAsync("userb@example.com", "Password123!", CancellationToken.None);
        var userB = await _userManager.FindByEmailAsync("userb@example.com");
        Assert.NotNull(userB);

        var linkResult = await _identityService.LinkExternalAsync(userB.Id, "GooglePlayGames", "shared_gpg_key", null, CancellationToken.None);

        Assert.False(linkResult.Succeeded);
        Assert.Contains(linkResult.Errors, e => e.Contains("already linked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LinkExternalAsync_UserAlreadyHasDifferentProviderKey_PreventsSilentReplacement()
    {
        await _identityService.RegisterAsync("singlelink@example.com", "Password123!", CancellationToken.None);
        var user = await _userManager.FindByEmailAsync("singlelink@example.com");
        Assert.NotNull(user);

        // Link first Google Play Games identity
        await _identityService.LinkExternalAsync(user.Id, "GooglePlayGames", "gpg_key_first", null, CancellationToken.None);

        // Attempt to link a DIFFERENT Google Play Games identity to the same local user
        var secondLinkResult = await _identityService.LinkExternalAsync(user.Id, "GooglePlayGames", "gpg_key_second", null, CancellationToken.None);

        Assert.False(secondLinkResult.Succeeded);
        Assert.Contains(secondLinkResult.Errors, e => e.Contains("already linked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LinkExternalAsync_NonExistentUserId_ReturnsFailure()
    {
        var result = await _identityService.LinkExternalAsync("non-existent-user-id", "GooglePlayGames", "gpg_key", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetInfoAsync_ExistingUser_ReturnsFullProfile()
    {
        var profile = new ExternalUserProfile(
            PlayerId: "gpg_info_test",
            DisplayName: "ProfileTester",
            FirstName: "Alice",
            SurName: "Smith",
            Email: "alice@example.com",
            EmailConfirmed: true,
            AvatarUrl: "https://avatar.test/alice.png");

        await _identityService.AuthenticateExternalAsync("GooglePlayGames", "gpg_info_test", profile, CancellationToken.None);
        var user = await _userManager.FindByLoginAsync("GooglePlayGames", "gpg_info_test");
        Assert.NotNull(user);

        var info = await _identityService.GetInfoAsync(user.Id, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("alice@example.com", info.Email);
        Assert.True(info.IsEmailConfirmed);
        Assert.Equal("ProfileTester", info.DisplayName);
        Assert.Equal("gpg_info_test", info.PlayerId);
        Assert.Equal("https://avatar.test/alice.png", info.AvatarUrl);
        Assert.Equal("Alice", info.FirstName);
        Assert.Equal("Smith", info.SurName);
    }
}
