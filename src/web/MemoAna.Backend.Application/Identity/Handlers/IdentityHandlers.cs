using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Application.Identity.Commands;
using MemoAna.Backend.Application.Identity.Queries;
using MemoAna.Backend.Application.Identity.Responses;
using Mediator;

namespace MemoAna.Backend.Application.Identity.Handlers;

/// <summary>Handles identity messages.</summary>
/// <param name="identityService">The identity service.</param>
/// <param name="googlePlayGamesService">The Google Play Games authentication service.</param>
public sealed class IdentityHandlers(
    IIdentityService identityService,
    IGooglePlayGamesAuthenticationService googlePlayGamesService)
    : IRequestHandler<RegisterCommand,
        IdentityResultResponse>,
      IRequestHandler<LoginCommand,
        Response<TokenResponse>>,
      IRequestHandler<GooglePlayGamesLoginCommand,
        Response<TokenResponse>>,
      IRequestHandler<RefreshTokenCommand,
        Response<TokenResponse>>,
      IRequestHandler<RevokeTokenCommand,
        Response<bool>>,
      IRequestHandler<ConfirmEmailCommand,
        Response<bool>>,
      IRequestHandler<ResendConfirmationEmailCommand,
        IdentityResultResponse>,
      IRequestHandler<ForgotPasswordCommand,
        IdentityResultResponse>,
      IRequestHandler<ResetPasswordCommand,
        IdentityResultResponse>,
      IRequestHandler<GetIdentityInfoQuery,
        Response<IdentityInfoResponse>>,
      IRequestHandler<UpdateIdentityInfoCommand,
        IdentityResultResponse>,
      IRequestHandler<ConfigureTwoFactorCommand,
        Response<TwoFactorResponse>>
{
    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        RegisterCommand request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.RegisterAsync(
                request.Email,
                request.Password,
                cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<Response<TokenResponse>>
        Handle(
            LoginCommand request,
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
            ? Response.Failure<TokenResponse>(
                ["Invalid credentials."])
            : Response.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<Response<TokenResponse>>
        Handle(
            GooglePlayGamesLoginCommand request,
            CancellationToken cancellationToken)
    {
        TokenResponse? result =
            await googlePlayGamesService.AuthenticateAsync(
                request.ServerAuthCode,
                request.RedirectUri,
                cancellationToken);

        return result is null
            ? Response.Failure<TokenResponse>(
                ["Google Play Games authentication failed."])
            : Response.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<Response<TokenResponse>>
        Handle(
            RefreshTokenCommand request,
            CancellationToken cancellationToken)
    {
        TokenResponse? result =
            await identityService.RefreshAsync(
                request.RefreshToken,
                cancellationToken);

        return result is null
            ? Response.Failure<TokenResponse>(
                ["Invalid refresh token."])
            : Response.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<Response<bool>> Handle(
        RevokeTokenCommand request,
        CancellationToken cancellationToken)
    {
        bool result = await identityService.RevokeAsync(
            request.AccessToken,
            cancellationToken);
        return Response.Success(result);
    }

    /// <inheritdoc />
    public async ValueTask<Response<bool>> Handle(
        ConfirmEmailCommand request,
        CancellationToken cancellationToken)
    {
        bool result =
            await identityService.ConfirmEmailAsync(
                request.UserId,
                request.Code,
                request.ChangedEmail,
                cancellationToken);
        return Response.Success(result);
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        ResendConfirmationEmailCommand request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.ResendConfirmationEmailAsync(
                request.Email,
                cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        ForgotPasswordCommand request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IdentityResultResponse>(
            identityService.ForgotPasswordAsync(
                request.Email,
                cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        ResetPasswordCommand request,
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
    public async ValueTask<Response<IdentityInfoResponse>>
        Handle(
            GetIdentityInfoQuery request,
            CancellationToken cancellationToken)
    {
        IdentityInfoResponse? result =
            await identityService.GetInfoAsync(
                request.UserId,
                cancellationToken);

        return result is null
            ? Response.Failure<IdentityInfoResponse>(
                ["User not found."])
            : Response.Success(result);
    }

    /// <inheritdoc />
    public ValueTask<IdentityResultResponse> Handle(
        UpdateIdentityInfoCommand request,
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
    public async ValueTask<Response<TwoFactorResponse>>
        Handle(
            ConfigureTwoFactorCommand request,
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
            ? Response.Failure<TwoFactorResponse>(
                ["Invalid 2FA configuration."])
            : Response.Success(result);
    }
}
