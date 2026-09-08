using MemoAna.Backend.Application.Identity.Responses;
using MemoAna.Backend.Infrastructure.Identity.Models;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests Identity edge-case business rules.</summary>
public sealed class IdentityServiceEdgeCaseTests
{
    [Fact]
    public async Task ResendConfirmation_ConfirmedUser_SucceedsWithoutSending()
    {
        using IdentityTestFixture fixture = new();
        _ = await fixture.CreateUserAsync("confirmed@example.com");

        IdentityResultResponse result = await fixture.Service
            .ResendConfirmationEmailAsync(
                "confirmed@example.com",
                CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ConfigureTwoFactor_NullEnable_ReturnsState()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "twofactor-null@example.com");

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, null, null, false, false,
                false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsTwoFactorEnabled);
        Assert.False(result.IsMachineRemembered);
    }

    [Fact]
    public async Task ConfigureTwoFactor_ResetSharedKey_Disables2Fa()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "twofactor-reset@example.com");
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, null, null, false, true,
                false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsTwoFactorEnabled);
        Assert.False(string.IsNullOrWhiteSpace(
            result.SharedKey));
    }

    [Fact]
    public async Task ConfigureTwoFactor_RecoveryCodesRequested_ReturnsCodes()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "twofactor-codes@example.com");

        TwoFactorResponse? result = await fixture.Service
            .ConfigureTwoFactorAsync(
                user.Id, null, null, true, false,
                false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.RecoveryCodes);
        Assert.Equal(10, result.RecoveryCodes!.Count);
    }

    [Fact]
    public async Task LoginAsync_WhitespaceTwoFactorCode_Fails()
    {
        using IdentityTestFixture fixture = new();
        User user = await fixture.CreateUserAsync(
            "twofactor-whitespace@example.com");
        _ = await fixture.UserManager.ResetAuthenticatorKeyAsync(user);
        _ = await fixture.UserManager.SetTwoFactorEnabledAsync(
            user, true);

        TokenResponse? result = await fixture.Service.LoginAsync(
            user.Email!, "Password1!", " ", null,
            CancellationToken.None);

        Assert.Null(result);
    }
}
