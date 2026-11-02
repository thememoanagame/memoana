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
      IRequestHandler<LinkGooglePlayGamesRequest,
        IdentityResultResponse>,
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
            ? ResponseBase<TokenResponse>.FailureResponse("Invalid credentials.")
            : ResponseBase<TokenResponse>.SuccessResponse(result);
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<TokenResponse>> Handle(
            GooglePlayGamesLoginRequest request,
            CancellationToken cancellationToken)
    {
        TokenResponse? result =
            await googlePlayGamesService.AuthenticateAsync(
                request.ServerAuthCode,
                request.RedirectUri,
                cancellationToken);

        return result is null
            ? ResponseBase<TokenResponse>.FailureResponse("Google Play Games authentication failed.")
            : ResponseBase<TokenResponse>.SuccessResponse(result);
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        LinkGooglePlayGamesRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            googlePlayGamesService.LinkAsync(
                request.UserId,
                request.ServerAuthCode,
                request.RedirectUri,
                cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<TokenResponse>> Handle(
            RefreshTokenRequest request,
            CancellationToken cancellationToken)
    {
        TokenResponse? result =
            await identityService.RefreshAsync(
                request.RefreshToken,
                cancellationToken);

        return result is null
            ? ResponseBase<TokenResponse>.FailureResponse("Invalid refresh token.")
            : ResponseBase<TokenResponse>.SuccessResponse(result);
    }

    /// <inheritdoc />
    public async ValueTask<ResponseBase<bool>> Handle(
        RevokeTokenRequest request,
        CancellationToken cancellationToken)
    {
        bool result = await identityService.RevokeAsync(
            request.AccessToken,
            cancellationToken);
        return ResponseBase<bool>.SuccessResponse(result);
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
        return ResponseBase<bool>.SuccessResponse(result);
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
    public async ValueTask<ResponseBase<IdentityInfoResponse>> Handle(
            GetIdentityInfoRequest request,
            CancellationToken cancellationToken)
    {
        IdentityInfoResponse? result =
            await identityService.GetInfoAsync(
                request.UserId,
                cancellationToken);

        return result is null
            ? ResponseBase<IdentityInfoResponse>.FailureResponse("User not found.")
            : ResponseBase<IdentityInfoResponse>.SuccessResponse(result);
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
    public async ValueTask<ResponseBase<TwoFactorResponse>> Handle(
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
            ? ResponseBase<TwoFactorResponse>.FailureResponse("Invalid 2FA configuration.")
            : ResponseBase<TwoFactorResponse>.SuccessResponse(result);
    }
}
