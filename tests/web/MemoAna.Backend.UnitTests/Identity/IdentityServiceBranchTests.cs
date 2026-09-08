using System.Security.Claims;
using MemoAna.Backend.Application.Identity.Responses;
using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Identity.Options;
using MemoAna.Backend.Infrastructure.Identity.Services;
using Microsoft.Extensions.Options;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Exercises less common Identity branches.</summary>
public sealed class IdentityServiceBranchTests
{
    [Fact]
    public async Task LoginAsync_TwoFactorRecoveryCode_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "recovery@example.com");
        _ = await fixture.UserManager.ResetAuthenticatorKeyAsync(user);
        string[]? codes = (await fixture.UserManager
            .GenerateNewTwoFactorRecoveryCodesAsync(user, 10))
            ?.ToArray();
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);

        string recoveryCode = codes![0];
        TokenResponse? result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, recoveryCode,
            CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task LoginAsync_TwoFactorInvalidCode_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "invalid-code@example.com");
        _ = await fixture.UserManager.ResetAuthenticatorKeyAsync(user);
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);

        TokenResponse? result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", "000000", null,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_TwoFactorMissingCode_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "missing-code@example.com");
        _ = await fixture.UserManager.ResetAuthenticatorKeyAsync(user);
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);

        TokenResponse? result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, null,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_LockedOutUser_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "locked@example.com");
        _ = await fixture.UserManager.SetLockoutEnabledAsync(
            user, true);
        _ = await fixture.UserManager.SetLockoutEndDateAsync(
            user, DateTimeOffset.UtcNow.AddMinutes(5));

        TokenResponse? result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, null,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_NotAllowedUser_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "not-allowed@example.com");
        user.EmailConfirmed = false;
        _ = await fixture.UserManager.UpdateAsync(user);
        fixture.UserManager.Options.SignIn.RequireConfirmedEmail =
            true;

        TokenResponse? result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, null,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_MissingUser_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        JwtTokenService tokenService = new(
            Options.Create(new JwtOptions
            {
                Key = "01234567890123456789012345678901",
                Issuer = "MemoAna.Backend.Tests",
                Audience = "MemoAna.Backend.Tests"
            }),
            new RevokedTokenStore());
        TokenResponse tokens = tokenService.CreateTokens(
            "missing-user",
            "missing@example.com",
            [],
            []);

        Assert.Null(await fixture.Service.RefreshAsync(
            tokens.RefreshToken, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_UserWithRole_ReturnsTokens()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "role-claims@example.com");
        Role role = new("Operator");
        Assert.True((await fixture.RoleManager.CreateAsync(role))
            .Succeeded);
        Assert.True((await fixture.RoleManager.AddClaimAsync(
            role,
            new Claim("permission", "read"))).Succeeded);
        Assert.True((await fixture.UserManager.AddToRoleAsync(
            user, role.Name!)).Succeeded);

        TokenResponse? result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, null,
            CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateInfoAsync_EmailConflict_Fails()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "owner@example.com");
        _ = await fixture.CreateUserAsync("taken@example.com");

        IdentityResultResponse result =
            await fixture.Service.UpdateInfoAsync(
                user.Id, "taken@example.com", null,
                "Password1!", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateInfoAsync_InvalidNewPassword_Fails()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "bad-password@example.com");

        IdentityResultResponse result =
            await fixture.Service.UpdateInfoAsync(
                user.Id, null, "x", "Password1!",
                CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ForgotPasswordAsync_UserWithoutPassword_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = new("external@example.com")
        {
            Email = "external@example.com",
            EmailConfirmed = true
        };
        Assert.True((await fixture.UserManager.CreateAsync(user))
            .Succeeded);

        IdentityResultResponse result =
            await fixture.Service.ForgotPasswordAsync(
                user.Email!, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_EnableWithValidCode_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "enable-2fa@example.com");
        string code = await fixture.GenerateValidAuthenticatorCodeAsync(user);

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, true, code, false, false, false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_EnableWithoutCode_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "enable-no-code@example.com");

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, true, null, false, false, false,
                CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_EnableInvalidCode_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "enable-bad-code@example.com");

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, true, "000000", false, false, false,
                CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_ResetKey_ReturnsKey()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "reset-key@example.com");

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, false, null, false, true, false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(
            result.SharedKey));
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_ResetRecoveryCodes_ReturnsCodes()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "reset-codes@example.com");

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, false, null, true, false, false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RecoveryCodes);
        Assert.NotEmpty(result.RecoveryCodes!);
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsTrueForExistingEmail()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("case@example.com");

        bool exists = await fixture.Service.EmailExistsAsync(
            "case@example.com", CancellationToken.None);

        Assert.True(exists);
    }
}
