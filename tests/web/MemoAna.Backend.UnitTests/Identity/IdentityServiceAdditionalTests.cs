using System.Security.Claims;
using MemoAna.Backend.Infrastructure.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests additional Identity branches.</summary>
public sealed class IdentityServiceAdditionalTests
{
    [Fact]
    public async Task LoginAsync_TwoFactorWithInvalidCode_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "2fa-invalid@example.com");
        _ = await fixture.UserManager.ResetAuthenticatorKeyAsync(user);
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);

        var result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", "000000", null,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_ValidCode_EnablesTwoFactor()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "2fa-valid@example.com");
        string code = await fixture.GenerateValidAuthenticatorCodeAsync(user);

        var result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, true, code, false, false, false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_ResetKeyAndCodes_ReturnsCodes()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "2fa-reset@example.com");

        var result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, false, null, true, true, false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.SharedKey);
        Assert.NotNull(result.RecoveryCodes);
    }

    [Fact]
    public async Task LoginAsync_WithRoleClaims_ReturnsTokens()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "role@example.com");
        Role role = new("Operator");
        IdentityResult roleResult = await fixture.RoleManager
            .CreateAsync(role);
        Assert.True(roleResult.Succeeded);
        IdentityResult claimResult = await fixture.RoleManager
            .AddClaimAsync(
                role,
                new Claim("permission", "read"));
        Assert.True(claimResult.Succeeded);
        IdentityResult userRoleResult =
            await fixture.UserManager.AddToRoleAsync(
                user,
                role.Name!);
        Assert.True(userRoleResult.Succeeded);

        var result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, null,
            CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateInfoAsync_EmailOnly_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "email-only@example.com");

        var result = await fixture.Service.UpdateInfoAsync(
            user.Id, "email-new@example.com", null,
            "Password1!", CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UpdateInfoAsync_PasswordOnly_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "password-only@example.com");

        var result = await fixture.Service.UpdateInfoAsync(
            user.Id, null, "NewPassword1!",
            "Password1!", CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UpdateInfoAsync_MissingUser_Fails()
    {
        using IdentityTestFixture fixture = new();

        var result = await fixture.Service.UpdateInfoAsync(
            "missing", "new@example.com", null,
            "Password1!", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidCode_Fails()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "reset-invalid@example.com");

        var result = await fixture.Service.ResetPasswordAsync(
            user.Email!, "invalid-code", "NewPassword1!",
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }
}
