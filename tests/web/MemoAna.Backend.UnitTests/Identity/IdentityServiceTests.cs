using MemoAna.Backend.Infrastructure.Identity.Models;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests the application Identity service.</summary>
public sealed class IdentityServiceTests
{
    [Fact]
    public async Task RegisterAsync_Succeeds()
    {
        using IdentityTestFixture fixture = new();

        var result = await fixture.Service.RegisterAsync(
            "new@example.com", "Password1!",
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RegisterAsync_Duplicate_Fails()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("same@example.com");

        var result = await fixture.Service.RegisterAsync(
            "same@example.com", "Password1!",
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task LoginAsync_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("login@example.com");

        var result = await fixture.Service.LoginAsync(
            "login@example.com", "Password1!", null, null,
            CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task LoginAsync_UnknownUser_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();

        Assert.Null(await fixture.Service.LoginAsync(
            "missing@example.com", "Password1!", null, null,
            CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("wrong@example.com");

        Assert.Null(await fixture.Service.LoginAsync(
            "wrong@example.com", "wrong", null, null,
            CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_SucceedsAndRevokesOldToken()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("refresh@example.com");
        var login = await fixture.Service.LoginAsync(
            "refresh@example.com", "Password1!", null, null,
            CancellationToken.None);

        var result = await fixture.Service.RefreshAsync(
            login!.RefreshToken, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEqual(login.RefreshToken,
            result.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_AccessToken_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("refresh2@example.com");
        var login = await fixture.Service.LoginAsync(
            "refresh2@example.com", "Password1!", null, null,
            CancellationToken.None);

        Assert.Null(await fixture.Service.RefreshAsync(
            login!.AccessToken, CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_ValidToken_ReturnsTrue()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("revoke@example.com");
        var login = await fixture.Service.LoginAsync(
            "revoke@example.com", "Password1!", null, null,
            CancellationToken.None);

        Assert.True(await fixture.Service.RevokeAsync(
            login!.AccessToken, CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_InvalidToken_ReturnsFalse()
    {
        using IdentityTestFixture fixture = new();

        Assert.False(await fixture.Service.RevokeAsync(
            "invalid", CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmEmailAsync_ConfirmationCode_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "confirm@example.com");
        string code = await fixture.UserManager
            .GenerateEmailConfirmationTokenAsync(user);

        Assert.True(await fixture.Service.ConfirmEmailAsync(
            user.Id, code, null, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmEmailAsync_EmailChange_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "change@example.com");
        string email = "changed@example.com";
        string code = await fixture.UserManager
            .GenerateChangeEmailTokenAsync(user, email);

        Assert.True(await fixture.Service.ConfirmEmailAsync(
            user.Id, code, email, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmEmailAsync_UnknownUser_ReturnsFalse()
    {
        using IdentityTestFixture fixture = new();

        Assert.False(await fixture.Service.ConfirmEmailAsync(
            "missing", "code", null,
            CancellationToken.None));
    }

    [Fact]
    public async Task ResendConfirmationEmailAsync_Unconfirmed_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "resend@example.com");
        user.EmailConfirmed = false;
        _ = await fixture.UserManager.UpdateAsync(user);

        var result = await fixture.Service
            .ResendConfirmationEmailAsync(
                user.Email!, CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResendConfirmationEmailAsync_Missing_Succeeds()
    {
        using IdentityTestFixture fixture = new();

        var result = await fixture.Service
            .ResendConfirmationEmailAsync(
                "missing@example.com", CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ForgotPasswordAsync_Existing_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("forgot@example.com");

        var result = await fixture.Service.ForgotPasswordAsync(
            "forgot@example.com", CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ForgotPasswordAsync_Missing_Succeeds()
    {
        using IdentityTestFixture fixture = new();

        var result = await fixture.Service.ForgotPasswordAsync(
            "missing@example.com", CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidCode_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "reset@example.com");
        string code = await fixture.UserManager
            .GeneratePasswordResetTokenAsync(user);

        var result = await fixture.Service.ResetPasswordAsync(
            user.Email!, code, "NewPassword1!",
            CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidUser_Fails()
    {
        using IdentityTestFixture fixture = new();

        var result = await fixture.Service.ResetPasswordAsync(
            "missing@example.com", "code", "NewPassword1!",
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetInfoAsync_ExistingUser_ReturnsInfo()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "info@example.com");

        var result = await fixture.Service.GetInfoAsync(
            user.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task GetInfoAsync_MissingUser_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();

        Assert.Null(await fixture.Service.GetInfoAsync(
            "missing", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateInfoAsync_InvalidPassword_Fails()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "update@example.com");

        var result = await fixture.Service.UpdateInfoAsync(
            user.Id, "new@example.com", "NewPassword1!",
            "wrong", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateInfoAsync_EmailAndPassword_Succeeds()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "update2@example.com");

        var result = await fixture.Service.UpdateInfoAsync(
            user.Id, "updated@example.com", "NewPassword1!",
            "Password1!", CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_MissingUser_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();

        Assert.Null(await fixture.Service.ConfigureTwoFactorAsync(
            "missing", null, null, false, false, false,
            CancellationToken.None));
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_Disable_ReturnsState()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "twofactor@example.com");

        var result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, false, null, false, false, false,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task ConfigureTwoFactorAsync_InvalidCode_ReturnsNull()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "twofactor2@example.com");

        var result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, true, "000000", false, false, false,
                CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsExpectedValues()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("exists@example.com");

        Assert.True(await fixture.Service.EmailExistsAsync(
            "exists@example.com", CancellationToken.None));
        Assert.False(await fixture.Service.EmailExistsAsync(
            "missing@example.com", CancellationToken.None));
    }
}
