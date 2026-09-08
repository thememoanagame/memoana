using MemoAna.Backend.Application.Identity.Requests;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests Identity request payloads.</summary>
public sealed class IdentityRequestTests
{
    [Fact]
    public void Requests_StoreProvidedValues()
    {
        RegisterRequest register = new(
            "user@example.com", "Password1!");
        LoginRequest login = new(
            "user@example.com", "Password1!",
            "123456", "recovery");
        RefreshRequest refresh = new("refresh");
        EmailRequest email = new("user@example.com");
        ResetPasswordRequest reset = new(
            "user@example.com", "code", "Password1!");
        InfoRequest info = new(
            "new@example.com", "Password2!", "Password1!");
        TwoFactorRequest twoFactor = new(
            true, "123456", true, true, true);

        Assert.Equal("user@example.com", register.Email);
        Assert.Equal("123456", login.TwoFactorCode);
        Assert.Equal("refresh", refresh.RefreshToken);
        Assert.Equal("user@example.com", email.Email);
        Assert.Equal("code", reset.ResetCode);
        Assert.Equal("new@example.com", info.NewEmail);
        Assert.True(twoFactor.ResetSharedKey);
    }
}
