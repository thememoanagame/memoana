using MemoAna.Auth.Abstract;
using MemoAna.Auth.Responses;
using MemoAna.Server.Application.Auth.Abstract;
using MemoAna.Server.Infrastructure.Auth.Models;
using MemoAna.Server.Infrastructure.Auth.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace MemoAna.Server.Infrastructure.Services.Concrete;

/// <summary>
/// Provides identity operations for users, authentication, account recovery, and two-factor authentication.
/// </summary>
/// <param name="userManager">The user manager used to manage application users.</param>
/// <param name="roleManager">The role manager used to manage application roles.</param>
/// <param name="signInManager">The sign-in manager used to validate user passwords and lockout state.</param>
/// <param name="tokenService">The service used to create and validate JWT tokens.</param>
/// <param name="revokedTokenStore">The store used to track revoked tokens.</param>
/// <param name="emailSender">The service used to send identity-related email messages.</param>
public sealed class IdentityService(
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    SignInManager<User> signInManager,
    IJwtTokenService tokenService,
    IRevokedTokenStore revokedTokenStore,
    IIdentityEmailSender emailSender) : IIdentityService
{
    /// <summary>
    /// Registers a new user and sends an email confirmation link.
    /// </summary>
    public async Task<IdentityResultResponse> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new User(email)
        {
            Email = email,
            DisplayName = string.Empty,
            FirstName = string.Empty,
            SurName = string.Empty,
            EmailConfirmed = false
        };

        IdentityResult result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return Failure(result);
        }

        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await emailSender.SendConfirmationAsync(
            email,
            $"/confirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}",
            cancellationToken);

        return IdentityResultResponse.Success();
    }

    /// <summary>
    /// Authenticates a user and issues JWT access and refresh tokens when all required authentication factors are valid.
    /// </summary>
    public async Task<TokenResponse?> LoginAsync(
        string email,
        string password,
        string? twoFactorCode,
        string? twoFactorRecoveryCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        SignInResult passwordResult = await signInManager.CheckPasswordSignInAsync(user, password, true);
        if (passwordResult.IsLockedOut || passwordResult.IsNotAllowed || !passwordResult.Succeeded)
        {
            return null;
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            var valid = !string.IsNullOrWhiteSpace(twoFactorCode)
                ? await userManager.VerifyTwoFactorTokenAsync(
                    user,
                    userManager.Options.Tokens.AuthenticatorTokenProvider,
                    twoFactorCode)
                : !string.IsNullOrWhiteSpace(twoFactorRecoveryCode) &&
                  (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, twoFactorRecoveryCode)).Succeeded;

            if (!valid)
            {
                return null;
            }
        }

        return await CreateUserTokensAsync(user);
    }

    /// <inheritdoc />
    public Task<TokenResponse?> AuthenticateExternalAsync(
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        return AuthenticateExternalAsync(loginProvider, providerKey, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TokenResponse?> AuthenticateExternalAsync(
        string loginProvider,
        string providerKey,
        ExternalUserProfile? profile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByLoginAsync(loginProvider, providerKey);
        if (user is null)
        {
            user = new User
            {
                UserName = $"{loginProvider}_{providerKey}",
                PlayerId = profile?.PlayerId ?? providerKey,
                DisplayName = profile?.DisplayName ?? string.Empty,
                AvatarUrl = profile?.AvatarUrl,
                FirstName = profile?.FirstName,
                SurName = profile?.SurName
            };

            // Respect existing email if verified by provider and not claimed by another local account.
            // Never create a synthetic email like @external.local.
            if (!string.IsNullOrWhiteSpace(profile?.Email))
            {
                User? existingByEmail = await userManager.FindByEmailAsync(profile.Email);
                if (existingByEmail is null)
                {
                    user.Email = profile.Email;
                    user.EmailConfirmed = profile.EmailConfirmed;
                }
            }

            byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
            string randomPassword = Convert.ToBase64String(randomBytes) + "A1a!";

            IdentityResult createResult = await userManager.CreateAsync(user, randomPassword);
            if (!createResult.Succeeded)
            {
                return null;
            }

            IdentityResult addLoginResult = await userManager.AddLoginAsync(
                user,
                new UserLoginInfo(loginProvider, providerKey, loginProvider));
            if (!addLoginResult.Succeeded)
            {
                _ = await userManager.DeleteAsync(user);
                return null;
            }
        }
        else if (profile is not null)
        {
            bool modified = false;
            if (string.IsNullOrWhiteSpace(user.PlayerId) && !string.IsNullOrWhiteSpace(profile.PlayerId))
            {
                user.PlayerId = profile.PlayerId;
                modified = true;
            }
            if (string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                user.DisplayName = profile.DisplayName;
                modified = true;
            }
            if (string.IsNullOrWhiteSpace(user.AvatarUrl) && !string.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                user.AvatarUrl = profile.AvatarUrl;
                modified = true;
            }
            if (string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(profile.FirstName))
            {
                user.FirstName = profile.FirstName;
                modified = true;
            }
            if (string.IsNullOrWhiteSpace(user.SurName) && !string.IsNullOrWhiteSpace(profile.SurName))
            {
                user.SurName = profile.SurName;
                modified = true;
            }

            if (modified)
            {
                await userManager.UpdateAsync(user);
            }
        }

        return await CreateUserTokensAsync(user);
    }

    /// <inheritdoc />
    public async Task<IdentityResultResponse> LinkExternalAsync(
        string userId,
        string loginProvider,
        string providerKey,
        ExternalUserProfile? profile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        cancellationToken.ThrowIfCancellationRequested();

        User? currentUser = await userManager.FindByIdAsync(userId);
        if (currentUser is null)
        {
            return IdentityResultResponse.Failure(["User not found."]);
        }

        // Check if another local user is already linked to this external provider key
        User? existingUser = await userManager.FindByLoginAsync(loginProvider, providerKey);
        if (existingUser is not null && !string.Equals(existingUser.Id, currentUser.Id, StringComparison.Ordinal))
        {
            return IdentityResultResponse.Failure(["This external identity is already linked to another user account."]);
        }

        if (existingUser is not null && string.Equals(existingUser.Id, currentUser.Id, StringComparison.Ordinal))
        {
            // Already linked to this user
            return IdentityResultResponse.Success();
        }

        // Prevent silent replacement of an existing provider identity
        IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(currentUser);
        UserLoginInfo? existingProvider = logins.FirstOrDefault(l => string.Equals(l.LoginProvider, loginProvider, StringComparison.OrdinalIgnoreCase));
        if (existingProvider is not null && !string.Equals(existingProvider.ProviderKey, providerKey, StringComparison.Ordinal))
        {
            return IdentityResultResponse.Failure(["An account from this provider is already linked to this user."]);
        }

        IdentityResult addLoginResult = await userManager.AddLoginAsync(
            currentUser,
            new UserLoginInfo(loginProvider, providerKey, loginProvider));

        if (!addLoginResult.Succeeded)
        {
            return Failure(addLoginResult);
        }

        // Update profile properties if empty
        bool modified = false;
        if (string.IsNullOrWhiteSpace(currentUser.PlayerId) && !string.IsNullOrWhiteSpace(profile?.PlayerId))
        {
            currentUser.PlayerId = profile.PlayerId;
            modified = true;
        }
        if (string.IsNullOrWhiteSpace(currentUser.DisplayName) && !string.IsNullOrWhiteSpace(profile?.DisplayName))
        {
            currentUser.DisplayName = profile.DisplayName;
            modified = true;
        }
        if (string.IsNullOrWhiteSpace(currentUser.AvatarUrl) && !string.IsNullOrWhiteSpace(profile?.AvatarUrl))
        {
            currentUser.AvatarUrl = profile.AvatarUrl;
            modified = true;
        }
        if (string.IsNullOrWhiteSpace(currentUser.FirstName) && !string.IsNullOrWhiteSpace(profile?.FirstName))
        {
            currentUser.FirstName = profile.FirstName;
            modified = true;
        }
        if (string.IsNullOrWhiteSpace(currentUser.SurName) && !string.IsNullOrWhiteSpace(profile?.SurName))
        {
            currentUser.SurName = profile.SurName;
            modified = true;
        }

        if (modified)
        {
            await userManager.UpdateAsync(currentUser);
        }

        return IdentityResultResponse.Success();
    }

    /// <summary>
    /// Exchanges a valid refresh token for a new access and refresh token pair and revokes the previous refresh token.
    /// </summary>
    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ClaimsPrincipal? principal = tokenService.ValidateToken(refreshToken);
        if (principal is null || !string.Equals(principal.FindFirstValue("token_type"), JwtTokenTypes.Refresh, StringComparison.Ordinal))
        {
            return null;
        }

        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        User? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var tokenId = tokenService.GetTokenId(refreshToken);
        DateTime? expiration = tokenService.GetExpiration(refreshToken);
        TokenResponse tokens = await CreateUserTokensAsync(user);

        if (!string.IsNullOrWhiteSpace(tokenId) && expiration.HasValue)
        {
            revokedTokenStore.Revoke(tokenId, expiration.Value);
        }

        return tokens;
    }

    /// <summary>
    /// Revokes a valid access token until its natural expiration.
    /// </summary>
    public Task<bool> RevokeAsync(string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ClaimsPrincipal? principal = tokenService.ValidateToken(accessToken);
        var tokenId = tokenService.GetTokenId(accessToken);
        DateTime? expiration = tokenService.GetExpiration(accessToken);

        if (principal is null || string.IsNullOrWhiteSpace(tokenId) || !expiration.HasValue)
        {
            return Task.FromResult(false);
        }

        revokedTokenStore.Revoke(tokenId, expiration.Value);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Confirms a user's email address or confirms a changed email address using a supplied Identity token.
    /// </summary>
    public async Task<bool> ConfirmEmailAsync(string userId, string code, string? changedEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        IdentityResult result = !string.IsNullOrWhiteSpace(changedEmail)
            ? await userManager.ChangeEmailAsync(user, changedEmail, code)
            : await userManager.ConfirmEmailAsync(user, code);

        return result.Succeeded;
    }

    /// <summary>
    /// Resends an email confirmation link when the specified user exists and remains unconfirmed.
    /// </summary>
    public async Task<IdentityResultResponse> ResendConfirmationEmailAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByEmailAsync(email);
        if (user is null || await userManager.IsEmailConfirmedAsync(user))
        {
            return IdentityResultResponse.Success();
        }

        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await emailSender.SendConfirmationAsync(
            email,
            $"/confirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}",
            cancellationToken);

        return IdentityResultResponse.Success();
    }

    /// <summary>
    /// Starts a password recovery operation for a user with a password.
    /// </summary>
    public async Task<IdentityResultResponse> ForgotPasswordAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByEmailAsync(email);
        if (user is null || !await userManager.HasPasswordAsync(user))
        {
            return IdentityResultResponse.Success();
        }

        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        await emailSender.SendPasswordResetAsync(
            email,
            $"/resetPassword?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}",
            cancellationToken);

        return IdentityResultResponse.Success();
    }

    /// <summary>
    /// Resets a user's password using a valid password-reset token.
    /// </summary>
    public async Task<IdentityResultResponse> ResetPasswordAsync(string email, string resetCode, string newPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return IdentityResultResponse.Failure(["Invalid password reset request."]);
        }

        IdentityResult result = await userManager.ResetPasswordAsync(user, resetCode, newPassword);
        return result.Succeeded ? IdentityResultResponse.Success() : Failure(result);
    }

    /// <summary>
    /// Gets identity information for a user.
    /// </summary>
    public async Task<IdentityInfoResponse?> GetInfoAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByIdAsync(userId);
        return user is null
            ? null
            : new IdentityInfoResponse(
                user.Email ?? string.Empty,
                await userManager.IsEmailConfirmedAsync(user),
                user.DisplayName,
                user.PlayerId,
                user.AvatarUrl,
                user.FirstName,
                user.SurName);
    }

    /// <summary>
    /// Updates a user's email address and password after validating the current password.
    /// </summary>
    public async Task<IdentityResultResponse> UpdateInfoAsync(string userId, string? newEmail, string? newPassword, string oldPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByIdAsync(userId);
        if (user is null || !await userManager.CheckPasswordAsync(user, oldPassword))
        {
            return IdentityResultResponse.Failure(["The current credentials are invalid."]);
        }

        if (!string.IsNullOrWhiteSpace(newEmail) && !string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            var emailCode = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
            IdentityResult emailResult = await userManager.ChangeEmailAsync(user, newEmail, emailCode);
            if (!emailResult.Succeeded)
            {
                return Failure(emailResult);
            }
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            IdentityResult passwordResult = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);
            if (!passwordResult.Succeeded)
            {
                return Failure(passwordResult);
            }
        }

        return IdentityResultResponse.Success();
    }

    /// <summary>
    /// Configures authenticator-based two-factor authentication and optionally rotates its recovery material.
    /// </summary>
    public async Task<TwoFactorResponse?> ConfigureTwoFactorAsync(string userId, bool? enable, string? twoFactorCode, bool resetRecoveryCodes, bool resetSharedKey, bool forgetMachine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        User? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        if (enable == true)
        {
            if (string.IsNullOrWhiteSpace(twoFactorCode) || !await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, twoFactorCode))
            {
                return null;
            }

            _ = await userManager.SetTwoFactorEnabledAsync(user, true);
        }
        else if (enable == false || resetSharedKey)
        {
            _ = await userManager.SetTwoFactorEnabledAsync(user, false);
        }

        if (resetSharedKey)
        {
            _ = await userManager.ResetAuthenticatorKeyAsync(user);
        }

        string[]? recoveryCodes = null;
        if (resetRecoveryCodes || (enable == true && await userManager.CountRecoveryCodesAsync(user) == 0))
        {
            recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
        }

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            _ = await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var recoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
        return new TwoFactorResponse(key, recoveryCodesLeft, recoveryCodes, await userManager.GetTwoFactorEnabledAsync(user), false);
    }

    private async Task<TokenResponse> CreateUserTokensAsync(User user)
    {
        IList<string> roles = await userManager.GetRolesAsync(user);
        IList<Claim> claims = await userManager.GetClaimsAsync(user);

        foreach (var roleName in roles)
        {
            Role? role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            claims = [.. claims, .. await roleManager.GetClaimsAsync(role)];
        }

        string identityName = !string.IsNullOrWhiteSpace(user.Email)
            ? user.Email
            : (!string.IsNullOrWhiteSpace(user.UserName) ? user.UserName : user.Id);

        return tokenService.CreateTokens(user.Id, identityName, roles, claims);
    }

    private static IdentityResultResponse Failure(IdentityResult result) => IdentityResultResponse.Failure(result.Errors.Select(error => error.Description));

    /// <inheritdoc/>
    public async Task<bool> EmailExistsAsync(
        string email,
        CancellationToken cancellationToken)
        => await userManager
            .Users
            .AsNoTracking()
            .AnyAsync(x =>
                x.Email == email,
                cancellationToken);
}

/// <summary>
/// Logs identity email messages instead of delivering them through an external email provider.
/// </summary>
public sealed partial class LoggingIdentityEmailSender(ILogger<LoggingIdentityEmailSender> logger) : IIdentityEmailSender
{
    public Task SendConfirmationAsync(string email, string confirmationLink, CancellationToken cancellationToken)
    {
        LogSendConfirmation(logger, email, confirmationLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken)
    {
        LogSendPassword(logger, email, resetLink);
        return Task.CompletedTask;
    }

    [LoggerMessage(0420, LogLevel.Information, "Identity confirmation link for {Email}: {Link}")]
    internal static partial void LogSendConfirmation(ILogger logger, string email, string link);

    [LoggerMessage(0240, LogLevel.Information, "Identity password reset link for {Email}: {Link}")]
    internal static partial void LogSendPassword(ILogger logger, string email, string link);
}
