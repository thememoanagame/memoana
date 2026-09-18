namespace MemoAna.Backend.Application.Identity.Requests;

/// <summary>Represents the registration request payload.</summary>
/// <param name="Email">The user's email address.</param>
/// <param name="Password">The user's password.</param>
public sealed record RegisterRequest(string Email, string Password);

/// <summary>Represents the login request payload.</summary>
/// <param name="Email">The user's email address.</param>
/// <param name="Password">The user's password.</param>
/// <param name="TwoFactorCode">The authenticator code, when required.</param>
/// <param name="TwoFactorRecoveryCode">The recovery code, when used instead of an authenticator code.</param>
public sealed record LoginRequest(string Email, string Password, string? TwoFactorCode = null, string? TwoFactorRecoveryCode = null);

/// <summary>Represents a refresh-token request payload.</summary>
/// <param name="RefreshToken">The refresh token to exchange.</param>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>Represents a Google Play Games authentication request payload.</summary>
/// <param name="ServerAuthCode">The server authentication code obtained from the client.</param>
/// <param name="RedirectUri">The application redirect uri</param>
public sealed record GooglePlayGamesLoginRequest(string ServerAuthCode, string RedirectUri);


/// <summary>Represents an email-only request payload.</summary>
/// <param name="Email">The email address.</param>
public sealed record EmailRequest(string Email);

/// <summary>Represents a password reset request payload.</summary>
/// <param name="Email">The user's email address.</param>
/// <param name="ResetCode">The password reset token.</param>
/// <param name="NewPassword">The replacement password.</param>
public sealed record ResetPasswordRequest(string Email, string ResetCode, string NewPassword);

/// <summary>Represents an authenticated identity information update payload.</summary>
/// <param name="NewEmail">The replacement email address.</param>
/// <param name="NewPassword">The replacement password.</param>
/// <param name="OldPassword">The current password.</param>
public sealed record InfoRequest(string? NewEmail, string? NewPassword, string OldPassword);

/// <summary>Represents an authenticator-based two-factor configuration request.</summary>
/// <param name="Enable">Whether to enable or disable two-factor authentication.</param>
/// <param name="TwoFactorCode">The authenticator code used when enabling two-factor authentication.</param>
/// <param name="ResetRecoveryCodes">Whether recovery codes should be regenerated.</param>
/// <param name="ResetSharedKey">Whether the shared authenticator key should be regenerated.</param>
/// <param name="ForgetMachine">Whether remembered-machine state should be cleared.</param>
public sealed record TwoFactorRequest(bool? Enable = null, string? TwoFactorCode = null, bool ResetRecoveryCodes = false, bool ResetSharedKey = false, bool ForgetMachine = false);

