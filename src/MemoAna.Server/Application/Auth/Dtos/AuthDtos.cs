namespace MemoAna.Auth.Dtos;


/// <summary>Represents the registration Dto payload.</summary>
/// <param name="Email">The user's email address.</param>
/// <param name="Password">The user's password.</param>
public sealed record RegisterDto(string Email, string Password);

/// <summary>Represents the login Dto payload.</summary>
/// <param name="Email">The user's email address.</param>
/// <param name="Password">The user's password.</param>
/// <param name="TwoFactorCode">The authenticator code, when required.</param>
/// <param name="TwoFactorRecoveryCode">The recovery code, when used instead of an authenticator code.</param>
public sealed record LoginDto(string Email, string Password, string? TwoFactorCode = null, string? TwoFactorRecoveryCode = null);

/// <summary>Represents a refresh-token Dto payload.</summary>
/// <param name="RefreshToken">The refresh token to exchange.</param>
public sealed record RefreshDto(string RefreshToken);

/// <summary>Represents a Google Play Games authentication Dto payload.</summary>
/// <param name="ServerAuthCode">The server authentication code obtained from the client.</param>
/// <param name="RedirectUri">The application redirect uri</param>
public sealed record GooglePlayGamesLoginDto(string ServerAuthCode, string RedirectUri);

/// <summary>Represents an email-only Dto payload.</summary>
/// <param name="Email">The email address.</param>
public sealed record EmailDto(string Email);

/// <summary>Represents a password reset Dto payload.</summary>
/// <param name="Email">The user's email address.</param>
/// <param name="ResetCode">The password reset token.</param>
/// <param name="NewPassword">The replacement password.</param>
public sealed record ResetPasswordDto(string Email, string ResetCode, string NewPassword);

/// <summary>Represents an authenticated identity information update payload.</summary>
/// <param name="NewEmail">The replacement email address.</param>
/// <param name="NewPassword">The replacement password.</param>
/// <param name="OldPassword">The current password.</param>
public sealed record InfoDto(string? NewEmail, string? NewPassword, string OldPassword);

/// <summary>Represents an authenticator-based two-factor configuration Dto.</summary>
/// <param name="Enable">Whether to enable or disable two-factor authentication.</param>
/// <param name="TwoFactorCode">The authenticator code used when enabling two-factor authentication.</param>
/// <param name="ResetRecoveryCodes">Whether recovery codes should be regenerated.</param>
/// <param name="ResetSharedKey">Whether the shared authenticator key should be regenerated.</param>
/// <param name="ForgetMachine">Whether remembered-machine state should be cleared.</param>
public sealed record TwoFactorDto(bool? Enable = null, string? TwoFactorCode = null, bool ResetRecoveryCodes = false, bool ResetSharedKey = false, bool ForgetMachine = false);

