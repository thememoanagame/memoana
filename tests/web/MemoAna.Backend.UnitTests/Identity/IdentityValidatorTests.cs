using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Application.Identity.Commands;
using MemoAna.Backend.Application.Identity.Responses;
using MemoAna.Backend.Application.Identity.Validators;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests Identity command validation rules.</summary>
public sealed class IdentityValidatorTests
{
    [Fact]
    public async Task RegisterValidator_ValidAndInvalidInputs()
    {
        FakeIdentityService service = new();
        RegisterCommandValidator validator = new(service);
        CancellationToken token =
            TestContext.Current.CancellationToken;

        FluentValidation.Results.ValidationResult valid =
            await validator.ValidateAsync(
                new RegisterCommand(
                    "user@example.com", "Password1$"), token);
        FluentValidation.Results.ValidationResult invalid =
            await validator.ValidateAsync(
                new RegisterCommand(
                    "user@example.com", "short"), token);

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public async Task LoginValidator_CoversCredentialsAnd2FaRules()
    {
        LoginCommandValidator validator = new();
        CancellationToken token =
            TestContext.Current.CancellationToken;

        Assert.True((await validator.ValidateAsync(
            new LoginCommand(
                "user@example.com", "Password1$"), token)).IsValid);
        Assert.False((await validator.ValidateAsync(
            new LoginCommand(
                "", "short", "123456", "code"), token))
            .IsValid);
        Assert.False((await validator.ValidateAsync(
            new LoginCommand(
                "user@example.com", "Password1$",
                "123456", "code"), token)).IsValid);
    }

    [Fact]
    public async Task SimpleTokenValidators_RejectEmptyValues()
    {
        CancellationToken token =
            TestContext.Current.CancellationToken;
        RefreshTokenCommandValidator refresh = new();
        RevokeTokenCommandValidator revoke = new();

        Assert.False((await refresh.ValidateAsync(
            new RefreshTokenCommand(""), token)).IsValid);
        Assert.True((await refresh.ValidateAsync(
            new RefreshTokenCommand("token"), token)).IsValid);
        Assert.False((await revoke.ValidateAsync(
            new RevokeTokenCommand(""), token)).IsValid);
        Assert.True((await revoke.ValidateAsync(
            new RevokeTokenCommand("token"), token)).IsValid);
    }

    [Fact]
    public async Task ConfirmEmailValidator_RequiresUserAndCode()
    {
        ConfirmEmailCommandValidator validator = new();
        CancellationToken token =
            TestContext.Current.CancellationToken;

        Assert.True((await validator.ValidateAsync(
            new ConfirmEmailCommand("user", "code"), token))
            .IsValid);
        Assert.False((await validator.ValidateAsync(
            new ConfirmEmailCommand("", ""), token)).IsValid);
    }

    [Fact]
    public async Task EmailValidators_RejectInvalidAddresses()
    {
        ResendConfirmationEmailCommandValidator resend = new();
        ForgotPasswordCommandValidator forgot = new();
        CancellationToken token =
            TestContext.Current.CancellationToken;

        Assert.True((await resend.ValidateAsync(
            new ResendConfirmationEmailCommand(
                "user@example.com"), token)).IsValid);
        Assert.False((await resend.ValidateAsync(
            new ResendConfirmationEmailCommand("bad"), token))
            .IsValid);
        Assert.True((await forgot.ValidateAsync(
            new ForgotPasswordCommand(
                "user@example.com"), token)).IsValid);
        Assert.False((await forgot.ValidateAsync(
            new ForgotPasswordCommand("bad"), token)).IsValid);
    }

    [Fact]
    public async Task ResetPasswordValidator_ValidatesAllFields()
    {
        ResetPasswordCommandValidator validator = new();
        CancellationToken token =
            TestContext.Current.CancellationToken;

        Assert.True((await validator.ValidateAsync(
            new ResetPasswordCommand(
                "user@example.com", "code", "Password1$"),
            token)).IsValid);
        Assert.False((await validator.ValidateAsync(
            new ResetPasswordCommand(
                "bad", "", "short"), token)).IsValid);
    }

    [Fact]
    public async Task UpdateIdentityValidator_OptionalFieldsHaveRules()
    {
        UpdateIdentityInfoCommandValidator validator = new();
        CancellationToken token =
            TestContext.Current.CancellationToken;

        Assert.True((await validator.ValidateAsync(
            new UpdateIdentityInfoCommand(
                "user", null, null, "Password1$"),
            token)).IsValid);
        Assert.False((await validator.ValidateAsync(
            new UpdateIdentityInfoCommand(
                "", "bad", "short", ""), token)).IsValid);
    }

    private sealed class FakeIdentityService :
        IIdentityService
    {
        public Task<IdentityResultResponse> RegisterAsync(
            string email, string password,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResultResponse.Success());

        public Task<bool> EmailExistsAsync(
            string email, CancellationToken cancellationToken) =>
            Task.FromResult(email == "existing@example.com");

        public Task<TokenResponse?> LoginAsync(
            string email, string password, string? twoFactorCode,
            string? twoFactorRecoveryCode,
            CancellationToken cancellationToken) =>
            Task.FromResult<TokenResponse?>(null);

        public Task<TokenResponse?> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken) =>
            Task.FromResult<TokenResponse?>(null);

        public Task<bool> RevokeAsync(
            string accessToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> ConfirmEmailAsync(
            string userId, string code, string? changedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IdentityResultResponse>
            ResendConfirmationEmailAsync(
                string email,
                CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResultResponse.Success());

        public Task<IdentityResultResponse> ForgotPasswordAsync(
            string email, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResultResponse.Success());

        public Task<IdentityResultResponse> ResetPasswordAsync(
            string email, string resetCode, string newPassword,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResultResponse.Success());

        public Task<IdentityInfoResponse?> GetInfoAsync(
            string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IdentityInfoResponse?>(null);

        public Task<IdentityResultResponse> UpdateInfoAsync(
            string userId, string? newEmail, string? newPassword,
            string oldPassword, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResultResponse.Success());

        public Task<TwoFactorResponse?> ConfigureTwoFactorAsync(
            string userId, bool? enable, string? twoFactorCode,
            bool resetRecoveryCodes, bool resetSharedKey,
            bool forgetMachine, CancellationToken cancellationToken) =>
            Task.FromResult<TwoFactorResponse?>(null);
    }
}
