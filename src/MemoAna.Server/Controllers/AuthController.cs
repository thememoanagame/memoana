using Mediator;
using MemoAna.Auth.Requests;
using MemoAna.Auth.Responses;
using MemoAna.Common.Base;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Relay.Requests;
using MemoAna.Server.Application.Relay.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace MemoAna.Server.Controllers;

/// <summary>
/// Provides endpoints for authentication and relay room management.
/// </summary>
/// <param name="mediator">Mediator used to dispatch authentication and room management requests.</param>
[Route("api/v1/auth")]
[ApiController]
[Tags("Authentication")]
public class AuthController(IMediator mediator) : ControllerBase
{
    /// <summary>Registers a new user.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new RegisterRequest(
                request.Email,
                request.Password),
            cancellationToken);
        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Authenticates a user.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ResponseBase<TokenResponse>>(401)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        ResponseBase<TokenResponse> result =
            await mediator.Send(
                new LoginRequest(
                    request.Email,
                    request.Password,
                    request.TwoFactorCode,
                    request.TwoFactorRecoveryCode),
                cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : Unauthorized(result);
    }

    /// <summary>Authenticates a user via Google Play Games.</summary>
    [HttpPost("login/google-play-games")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ResponseBase<TokenResponse>>(401)]
    public async Task<IActionResult> GooglePlayGamesLogin(
        GooglePlayGamesLoginRequest request,
        CancellationToken cancellationToken)
    {
        ResponseBase<TokenResponse> result =
            await mediator.Send(
                new GooglePlayGamesLoginRequest(
                    request.ServerAuthCode,
                    request.RedirectUri),
                cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : Unauthorized(result);
    }

    /// <summary>Refreshes authentication tokens.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ResponseBase<TokenResponse>>(401)]
    public async Task<IActionResult> Refresh(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        ResponseBase<TokenResponse> result = await mediator.Send(
            new RefreshTokenRequest(request.RefreshToken),
            cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : Unauthorized(result);
    }

    /// <summary>Revokes the current access token.</summary>
    [HttpPost("revoke")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType<ResponseBase<bool>>(401)]
    public async Task<IActionResult> Revoke(
        CancellationToken cancellationToken)
    {
        string token = Request.Headers.Authorization
            .ToString()
            .Replace(
                "Bearer ",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);

        ResponseBase<bool> result = await mediator.Send(
            new RevokeTokenRequest(token),
            cancellationToken);

        return result.Data == true
            ? Ok()
            : Unauthorized(result);
    }

    /// <summary>Confirms a user's email.</summary>
    [HttpGet("confirmEmail")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<ResponseBase<bool>>(400)]
    public async Task<IActionResult> ConfirmEmail(
        [FromQuery] string userId,
        [FromQuery] string code,
        [FromQuery] string? changedEmail,
        CancellationToken cancellationToken)
    {
        ResponseBase<bool> result = await mediator.Send(
            new ConfirmEmailRequest(
                userId,
                code,
                changedEmail),
            cancellationToken);
        return result.Data == true
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Resends the confirmation email.</summary>
    [HttpPost("resendConfirmationEmail")]
    [AllowAnonymous]
    [ProducesResponseType<IdentityResultResponse>(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> ResendConfirmationEmail(
        EmailRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ResendConfirmationEmailRequest(
                request.Email),
            cancellationToken);
        return result.Succeeded
            ? Ok(result)
            : BadRequest(result);
    }

    /// <summary>Starts password recovery.</summary>
    [HttpPost("forgotPassword")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> ForgotPassword(
        EmailRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ForgotPasswordRequest(request.Email),
            cancellationToken);
        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Resets a password.</summary>
    [HttpPost("resetPassword")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ResetPasswordRequest(
                request.Email,
                request.ResetCode,
                request.NewPassword),
            cancellationToken);
        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Gets authenticated identity information.</summary>
    [HttpGet("manage/info")]
    [Authorize]
    [ProducesResponseType<IdentityInfoResponse>(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType<ResponseBase<IdentityInfoResponse>>(404)]
    public async Task<IActionResult> GetInfo(
        CancellationToken cancellationToken)
    {
        string? userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        ResponseBase<IdentityInfoResponse> result =
            await mediator.Send(
                new GetIdentityInfoRequest(userId),
                cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : NotFound(result);
    }

    /// <summary>Updates authenticated identity information.</summary>
    [HttpPost("manage/info")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UpdateInfo(
        InfoRequest request,
        CancellationToken cancellationToken)
    {
        string? userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        IdentityResultResponse result = await mediator.Send(
            new UpdateIdentityInfoRequest(
                userId,
                request.NewEmail,
                request.NewPassword,
                request.OldPassword),
            cancellationToken);
        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Configures two-factor authentication.</summary>
    [HttpPost("manage/2fa")]
    [Authorize]
    [ProducesResponseType<TwoFactorResponse>(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ConfigureTwoFactor(
        TwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        string? userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        ResponseBase<TwoFactorResponse> result =
            await mediator.Send(
                new ConfigureTwoFactorRequest(
                    userId,
                    request.Enable,
                    request.TwoFactorCode,
                    request.ResetRecoveryCodes,
                    request.ResetSharedKey,
                    request.ForgetMachine),
                cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : BadRequest(result);
    }

    private string? GetUserId()
    {
        return User.FindFirstValue(
                   JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue(
                   ClaimTypes.NameIdentifier);
    }
}
