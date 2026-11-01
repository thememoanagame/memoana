using Mediator;
using MemoAna.Auth.Abstract;
using MemoAna.Auth.Requests;
using MemoAna.Auth.Responses;
using MemoAna.Common.Base;
namespace MemoAna.Auth.Handlers;

/// <summary>Handles identity messages.</summary>
/// <param name="identityService">The identity service.</param>
/// <param name="googlePlayGamesService">The Google Play Games authentication service.</param>
public sealed class AuthHandlers(
    IIdentityService identityService,
    IGooglePlayGamesAuthenticationService googlePlayGamesService)
    : IRequestHandler<RegisterRequest,
        IdentityResultResponse>,
      IRequestHandler<LoginRequest,
        ResponseBase<TokenResponse>>,
      IRequestHandler<GooglePlayGamesLoginRequest,
        ResponseBase<TokenResponse>>,
      IRequestHandler<RefreshTokenRequest,
        ResponseBase<TokenResponse>>,
      IRequestHandler<RevokeTokenRequest,
        ResponseBase<bool>>,
      IRequestHandler<ConfirmEmailRequest,
        ResponseBase<bool>>,
      IRequestHandler<ResendConfirmationEmailRequest,
        IdentityResultResponse>,
      IRequestHandler<ForgotPasswordRequest,
        IdentityResultResponse>,
      IRequestHandler<ResetPasswordRequest,
        IdentityResultResponse>,
      IRequestHandler<GetIdentityInfoRequest,
        ResponseBase<IdentityInfoResponse>>,
      IRequestHandler<UpdateIdentityInfoRequest,
        IdentityResultResponse>,
      IRequestHandler<ConfigureTwoFactorRequest,
        ResponseBase<TwoFactorResponse>>
{
    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.RegisterAsync(
                request.Email,
                request.Password,
                cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<TokenResponse>> Handle(
            LoginRequest request,
            CancellationToken cancellationToken)
    {
        TokenResponse? result =
            await identityService.LoginAsync(
                request.Email,
                request.Password,
                request.TwoFactorCode,
                request.TwoFactorRecoveryCode,
                cancellationToken);

        return result is null
            ? ResponseFactory.Failure<TokenResponse>(
                ["Invalid credentials."])
            : ResponseFactory.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<TokenResponse>>
        Handle(
            GooglePlayGamesLoginRequest request,
            CancellationToken cancellationToken)
    {
        TokenResponse? result =
            await googlePlayGamesService.AuthenticateAsync(
                request.ServerAuthCode,
                request.RedirectUri,
                cancellationToken);

        return result is null
            ? ResponseFactory.Failure<TokenResponse>(
                ["Google Play Games authentication failed."])
            : ResponseFactory.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<TokenResponse>>
        Handle(
            RefreshTokenRequest request,
            CancellationToken cancellationToken)
    {
        TokenResponse? result =
            await identityService.RefreshAsync(
                request.RefreshToken,
                cancellationToken);

        return result is null
            ? ResponseFactory.Failure<TokenResponse>(
                ["Invalid refresh token."])
            : ResponseFactory.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<bool>> Handle(
        RevokeTokenRequest request,
        CancellationToken cancellationToken)
    {
        bool result = await identityService.RevokeAsync(
            request.AccessToken,
            cancellationToken);
        return ResponseFactory.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<bool>> Handle(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        bool result =
            await identityService.ConfirmEmailAsync(
                request.UserId,
                request.Code,
                request.ChangedEmail,
                cancellationToken);
        return ResponseFactory.Success(result);
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        ResendConfirmationEmailRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.ResendConfirmationEmailAsync(
                request.Email,
                cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.ForgotPasswordAsync(
                request.Email,
                cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.ResetPasswordAsync(
                request.Email,
                request.ResetCode,
                request.NewPassword,
                cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<IdentityInfoResponse>>
        Handle(
            GetIdentityInfoRequest request,
            CancellationToken cancellationToken)
    {
        IdentityInfoResponse? result =
            await identityService.GetInfoAsync(
                request.UserId,
                cancellationToken);

        return result is null
            ? ResponseFactory.Failure<IdentityInfoResponse>(
                ["User not found."])
            : ResponseFactory.Success(result);
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        UpdateIdentityInfoRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.UpdateInfoAsync(
                request.UserId,
                request.NewEmail,
                request.NewPassword,
                request.OldPassword,
                cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<TwoFactorResponse>>
        Handle(
            ConfigureTwoFactorRequest request,
            CancellationToken cancellationToken)
    {
        TwoFactorResponse? result =
            await identityService.ConfigureTwoFactorAsync(
                request.UserId,
                request.Enable,
                request.TwoFactorCode,
                request.ResetRecoveryCodes,
                request.ResetSharedKey,
                request.ForgetMachine,
                cancellationToken);

        return result is null
            ? ResponseFactory.Failure<TwoFactorResponse>(
                ["Invalid 2FA configuration."])
            : ResponseFactory.Success(result);
    }
}
