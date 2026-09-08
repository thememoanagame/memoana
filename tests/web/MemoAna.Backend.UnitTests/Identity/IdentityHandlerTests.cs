using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Application.Identity.Commands;
using MemoAna.Backend.Application.Identity.Handlers;
using MemoAna.Backend.Application.Identity.Queries;
using MemoAna.Backend.Application.Identity.Responses;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests Identity Mediator handlers.</summary>
public sealed class IdentityHandlerTests
{
    [Fact]
    public async Task Handlers_ForwardSuccessfulOperations()
    {
        FakeIdentityService identityService = new();
        FakeGooglePlayGamesAuthenticationService googleService = new();
        IdentityHandlers handlers = new(identityService, googleService);
        CancellationToken token = CancellationToken.None;

        Assert.True((await handlers.Handle(
            new RegisterCommand("a@b.com", "Password1!"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new LoginCommand("a@b.com", "Password1!"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new RefreshTokenCommand("refresh"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new RevokeTokenCommand("access"), token))
            .Data);
        Assert.True((await handlers.Handle(
            new ConfirmEmailCommand("id", "code"), token))
            .Data);
        Assert.True((await handlers.Handle(
            new ResendConfirmationEmailCommand("a@b.com"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new ForgotPasswordCommand("a@b.com"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new ResetPasswordCommand(
                "a@b.com", "code", "Password1!"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new GetIdentityInfoQuery("id"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new UpdateIdentityInfoCommand(
                "id", null, null, "Password1!"), token))
            .Succeeded);
        Assert.True((await handlers.Handle(
            new ConfigureTwoFactorCommand(
                "id", false, null, false, false, false), token))
            .Succeeded);
    }

    [Fact]
    public async Task Handlers_MapMissingResultsToFailures()
    {
        FakeIdentityService identityService = new()
        {
            ReturnData = false
        };

        FakeGooglePlayGamesAuthenticationService googleService = new()
        {
            ReturnData = false
        };
        IdentityHandlers handlers = new(identityService, googleService);
        CancellationToken token = CancellationToken.None;

        Assert.False((await handlers.Handle(
            new LoginCommand("a@b.com", "bad"), token))
            .Succeeded);
        Assert.False((await handlers.Handle(
            new RefreshTokenCommand("bad"), token))
            .Succeeded);
        Assert.False((await handlers.Handle(
            new GetIdentityInfoQuery("missing"), token))
            .Succeeded);
        Assert.False((await handlers.Handle(
            new ConfigureTwoFactorCommand(
                "id", true, null, false, false, false), token))
            .Succeeded);
        Assert.False((await handlers.Handle(
            new RevokeTokenCommand("access"), token))
            .Data);
        Assert.False((await handlers.Handle(
            new ConfirmEmailCommand("id", "code"), token))
            .Data);
    }

    private sealed class FakeGooglePlayGamesAuthenticationService : IGooglePlayGamesAuthenticationService
    {
        public bool ReturnData { get; set; } = true;
        private static TokenResponse Token =>
                    new("Bearer", "access", 900, "refresh");
        public Task<TokenResponse?> AuthenticateAsync(
            string serverAuthCode,
            string redirectUri,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ReturnData ? Token : null);
        }

    }
    private sealed class FakeIdentityService : IIdentityService
    {
        public bool ReturnData { get; set; } = true;

        public Task<IdentityResultResponse> RegisterAsync(
            string email, string password,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResultResponse.Success());

        public Task<bool> EmailExistsAsync(
            string email, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<TokenResponse?> LoginAsync(
            string email, string password, string? twoFactorCode,
            string? twoFactorRecoveryCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReturnData ? Token : null);

        public Task<TokenResponse?> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReturnData ? Token : null);

        public Task<bool> RevokeAsync(
            string accessToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReturnData);

        public Task<bool> ConfirmEmailAsync(
            string userId, string code, string? changedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReturnData);

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
            Task.FromResult(ReturnData
                ? new IdentityInfoResponse(
                    "a@b.com", true)
                : null);

        public Task<IdentityResultResponse> UpdateInfoAsync(
            string userId, string? newEmail, string? newPassword,
            string oldPassword, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResultResponse.Success());

        public Task<TwoFactorResponse?> ConfigureTwoFactorAsync(
            string userId, bool? enable, string? twoFactorCode,
            bool resetRecoveryCodes, bool resetSharedKey,
            bool forgetMachine, CancellationToken cancellationToken) =>
            Task.FromResult(ReturnData
                ? new TwoFactorResponse(
                    "key", 10, [], false, false)
                : null);

        private static TokenResponse Token =>
            new("Bearer", "access", 900, "refresh");
    }
}
