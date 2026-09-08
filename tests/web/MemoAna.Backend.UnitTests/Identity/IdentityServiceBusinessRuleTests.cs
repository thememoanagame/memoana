using MemoAna.Backend.Infrastructure.Identity.Models;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Exercises additional Identity business branches.</summary>
public sealed class IdentityServiceBusinessRuleTests
{
    [Fact]
    public async Task LoginAsync_AuthenticatorCode_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "authenticator@example.com");
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);
        string code = await fixture.GenerateValidAuthenticatorCodeAsync(user);

        var result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", code, null,
            CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task LoginAsync_InvalidAuthenticatorCode_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "bad-authenticator@example.com");
        _ = await fixture.UserManager.ResetAuthenticatorKeyAsync(user);
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);

        var result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", "000000", null,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_LockedUser_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "locked@example.com");
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5);
        _ = await fixture.UserManager.UpdateAsync(user);

        var result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, null,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_UnconfirmedUser_WhenRequired_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "unconfirmed@example.com");
        user.EmailConfirmed = false;
        _ = await fixture.UserManager.UpdateAsync(user);
        fixture.UserManager.Options.SignIn
            .RequireConfirmedEmail = true;

        var result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", null, null,
            CancellationToken.None);

        Assert.Null(result);
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
        Assert.True((await fixture.UserManager.AddLoginAsync(
            user,
            new Microsoft.AspNetCore.Identity.UserLoginInfo(
                "TestProvider",
                "external-id",
                "TestProvider"))).Succeeded);

        var result = await fixture.Service.ForgotPasswordAsync(
            user.Email!, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_EnableWithValidCode_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "enable2fa@example.com");
        string code = await fixture.GenerateValidAuthenticatorCodeAsync(user);

        var result = await fixture.Service
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

        var result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, true, null, false, false, false,
                CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_EnableCreatesRecoveryCodes()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "enable-codes@example.com");
        string code = await fixture.GenerateValidAuthenticatorCodeAsync(user);

        var result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, true, code, false, false, false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RecoveryCodes);
        Assert.NotEmpty(result.RecoveryCodes!);
    }
}
