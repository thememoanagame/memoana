using MemoAna.Backend.Application.Identity.Responses;

namespace MemoAna.Backend.Application.Identity.Abstractions;

/// <summary> Identity Service abstraction </summary>
public interface IIdentityService
{
     /// <summary>Registers a user and starts email confirmation.</summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The registration result.</returns>
    Task<IdentityResultResponse> RegisterAsync(string email, string password, CancellationToken cancellationToken);

   /// <summary>
   /// Checks if an email is already registered
   /// </summary>
   /// <param name="email">The email to check if it exists</param>
   /// <param name="cancellationToken">An cancellation token for the operation.</param>
   /// <returns>true if email exists; otherwise, false</returns>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    /// <summary>Authenticates a user and issues JWT tokens when all required factors are valid.</summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="twoFactorCode">The authenticator code, when two-factor authentication is enabled.</param>
    /// <param name="twoFactorRecoveryCode">The recovery code, when a recovery code is used instead of an authenticator code.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The token pair, or <see langword="null"/> when authentication fails.</returns>
    Task<TokenResponse?> LoginAsync(string email, string password, string? twoFactorCode, string? twoFactorRecoveryCode, CancellationToken cancellationToken);

    /// <summary>Exchanges a valid refresh token for a new token pair.</summary>
    /// <param name="refreshToken">The refresh token to exchange.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The new token pair, or <see langword="null"/> when the refresh token is invalid.</returns>
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes an access token until its natural expiration.</summary>
    /// <param name="accessToken">The access token to revoke.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns><see langword="true"/> when revocation succeeds; otherwise <see langword="false"/>.</returns>
    Task<bool> RevokeAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>Confirms a user's email address or a changed email address.</summary>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="code">The confirmation token.</param>
    /// <param name="changedEmail">The replacement email address when confirming an email change.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns><see langword="true"/> when confirmation succeeds; otherwise <see langword="false"/>.</returns>
    Task<bool> ConfirmEmailAsync(string userId, string code, string? changedEmail, CancellationToken cancellationToken);

    /// <summary>Resends email confirmation when required.</summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The resend operation result.</returns>
    Task<IdentityResultResponse> ResendConfirmationEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Starts password recovery for a password-bearing user.</summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A result that does not reveal whether an email is registered.</returns>
    Task<IdentityResultResponse> ForgotPasswordAsync(string email, CancellationToken cancellationToken);

    /// <summary>Resets a user's password with a valid reset token.</summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="resetCode">The password reset token.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The reset operation result.</returns>
    Task<IdentityResultResponse> ResetPasswordAsync(string email, string resetCode, string newPassword, CancellationToken cancellationToken);

    /// <summary>Gets basic identity information for a user.</summary>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The identity information, or <see langword="null"/> when the user does not exist.</returns>
    Task<IdentityInfoResponse?> GetInfoAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Updates identity information after validating the current password.</summary>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="newEmail">The replacement email address, or <see langword="null"/> to keep the current address.</param>
    /// <param name="newPassword">The replacement password, or <see langword="null"/> to keep the current password.</param>
    /// <param name="oldPassword">The current password.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The update operation result.</returns>
    Task<IdentityResultResponse> UpdateInfoAsync(string userId, string? newEmail, string? newPassword, string oldPassword, CancellationToken cancellationToken);

    /// <summary>Configures authenticator-based two-factor authentication and recovery material.</summary>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="enable">Whether to enable or disable two-factor authentication.</param>
    /// <param name="twoFactorCode">The authenticator code used to validate enabling two-factor authentication.</param>
    /// <param name="resetRecoveryCodes">Whether recovery codes should be regenerated.</param>
    /// <param name="resetSharedKey">Whether the authenticator shared key should be regenerated.</param>
    /// <param name="forgetMachine">Whether the current machine should be forgotten.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The current configuration, or <see langword="null"/> when the operation cannot be completed.</returns>
    Task<TwoFactorResponse?> ConfigureTwoFactorAsync(string userId, bool? enable, string? twoFactorCode, bool resetRecoveryCodes, bool resetSharedKey, bool forgetMachine, CancellationToken cancellationToken);

 }
