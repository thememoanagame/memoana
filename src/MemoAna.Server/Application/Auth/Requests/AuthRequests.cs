using MemoAna.Auth.Responses;
using Mediator;
using MemoAna.Common.Base;
namespace MemoAna.Auth.Requests;


/// <summary>Requests registration of a user.</summary>
/// <param name="Email">The user email.</param>
/// <param name="Password">The user password.</param>
public sealed record RegisterRequest(
    string Email,
    string Password)
    : IRequest<IdentityResultResponse>;

/// <summary>Requests user authentication.</summary>
/// <param name="Email">The user email.</param>
/// <param name="Password">The password.</param>
/// <param name="TwoFactorCode">The 2FA code.</param>
/// <param name="TwoFactorRecoveryCode">
/// The 2FA recovery code.
/// </param>
public sealed record LoginRequest(
    string Email,
    string Password,
    string? TwoFactorCode = null,
    string? TwoFactorRecoveryCode = null)
    : IRequest<ResponseBase<TokenResponse>>;

/// <summary>Requests Google Play Games user authentication.</summary>
/// <param name="ServerAuthCode">The server authentication code from the client.</param>
/// <param name="RedirectUri">The application redirect uri</param>
public sealed record GooglePlayGamesLoginRequest(
    string ServerAuthCode, string RedirectUri)
    : IRequest<ResponseBase<TokenResponse>>;


/// <summary>Requests a refresh token exchange.</summary>
/// <param name="RefreshToken">The refresh token.</param>
public sealed record RefreshTokenRequest(
    string RefreshToken)
    : IRequest<ResponseBase<TokenResponse>>;

/// <summary>Requests access token revocation.</summary>
/// <param name="AccessToken">The access token.</param>
public sealed record RevokeTokenRequest(
    string AccessToken)
    : IRequest<ResponseBase<bool>>;

/// <summary>Requests email confirmation.</summary>
/// <param name="UserId">The user identifier.</param>
/// <param name="Code">The confirmation code.</param>
/// <param name="ChangedEmail">The changed email.</param>
public sealed record ConfirmEmailRequest(
    string UserId,
    string Code,
    string? ChangedEmail = null)
    : IRequest<ResponseBase<bool>>;

/// <summary>Requests confirmation email resend.</summary>
/// <param name="Email">The user email.</param>
public sealed record ResendConfirmationEmailRequest(
    string Email)
    : IRequest<IdentityResultResponse>;

/// <summary>Starts password recovery.</summary>
/// <param name="Email">The user email.</param>
public sealed record ForgotPasswordRequest(
    string Email)
    : IRequest<IdentityResultResponse>;

/// <summary>Requests a password reset.</summary>
/// <param name="Email">The user email.</param>
/// <param name="ResetCode">The reset code.</param>
/// <param name="NewPassword">The new password.</param>
public sealed record ResetPasswordRequest(
    string Email,
    string ResetCode,
    string NewPassword)
    : IRequest<IdentityResultResponse>;

/// <summary>Requests identity information update.</summary>
/// <param name="UserId">The user identifier.</param>
/// <param name="NewEmail">The new email.</param>
/// <param name="NewPassword">The new password.</param>
/// <param name="OldPassword">The current password.</param>
public sealed record UpdateIdentityInfoRequest(
    string UserId,
    string? NewEmail,
    string? NewPassword,
    string OldPassword)
    : IRequest<IdentityResultResponse>;

/// <summary>Requests 2FA configuration.</summary>
/// <param name="UserId">The user identifier.</param>
/// <param name="Enable">Whether 2FA is enabled.</param>
/// <param name="TwoFactorCode">The authenticator code.</param>
/// <param name="ResetRecoveryCodes">Whether codes reset.</param>
/// <param name="ResetSharedKey">Whether the key resets.</param>
/// <param name="ForgetMachine">Whether machine state resets.</param>
public sealed record ConfigureTwoFactorRequest(
    string UserId,
    bool? Enable,
    string? TwoFactorCode,
    bool ResetRecoveryCodes,
    bool ResetSharedKey,
    bool ForgetMachine)
    : IRequest<ResponseBase<TwoFactorResponse>>;


/// <summary>Requests identity information.</summary>
/// <param name="UserId">The user identifier.</param>
public sealed record GetIdentityInfoRequest(string UserId)
    : IRequest<ResponseBase<IdentityInfoResponse>>;
