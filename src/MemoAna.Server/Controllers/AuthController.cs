using Mediator;
using MemoAna.Auth.Dtos;
using MemoAna.Auth.Requests;
using MemoAna.Auth.Responses;
using MemoAna.Common.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MemoAna.Server.Controllers;

/// <summary>
/// Provides endpoints for authentication, external identity federation, and identity management.
/// </summary>
/// <param name="mediator">Mediator used to dispatch authentication and identity requests.</param>
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
        [FromBody] RegisterDto dto,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new RegisterRequest(
                dto.Email,
                dto.Password),
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
        [FromBody] LoginDto dto,
        CancellationToken cancellationToken)
    {
        ResponseBase<TokenResponse> result =
            await mediator.Send(
                new LoginRequest(
                    dto.Email,
                    dto.Password,
                    dto.TwoFactorCode,
                    dto.TwoFactorRecoveryCode),
                cancellationToken);
        return result.Success
            ? Ok(result.Value)
            : Unauthorized(result);
    }

    /// <summary>Authenticates a user via Google Play Games.</summary>
    [HttpPost("login/google-play-games")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ResponseBase<TokenResponse>>(401)]
    public async Task<IActionResult> GooglePlayGamesLogin(
        [FromBody] GooglePlayGamesLoginDto dto,
        CancellationToken cancellationToken)
    {
        ResponseBase<TokenResponse> result =
            await mediator.Send(
                new GooglePlayGamesLoginRequest(
                    dto.ServerAuthCode,
                    dto.RedirectUri),
                cancellationToken);
        return result.Success
            ? Ok(result.Value)
            : Unauthorized(result);
    }

    /// <summary>Links a Google Play Games identity to an authenticated account.</summary>
    [HttpPost("manage/link/google-play-games")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> LinkGooglePlayGames(
        [FromBody] GooglePlayGamesLoginDto dto,
        CancellationToken cancellationToken)
    {
        string? userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        IdentityResultResponse result = await mediator.Send(
            new LinkGooglePlayGamesRequest(
                userId,
                dto.ServerAuthCode,
                dto.RedirectUri),
            cancellationToken);

        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Refreshes authentication tokens.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ResponseBase<TokenResponse>>(401)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshDto dto,
        CancellationToken cancellationToken)
    {
        ResponseBase<TokenResponse> result = await mediator.Send(
            new RefreshTokenRequest(dto.RefreshToken),
            cancellationToken);
        return result.Success
            ? Ok(result.Value)
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
                StringComparison.OrdinalIgnoreCase)
            .Trim();

        ResponseBase<bool> result = await mediator.Send(
            new RevokeTokenRequest(token),
            cancellationToken);

        return result.Value == true
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
        return result.Value == true
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Resends the confirmation email.</summary>
    [HttpPost("resendConfirmationEmail")]
    [AllowAnonymous]
    [ProducesResponseType<IdentityResultResponse>(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> ResendConfirmationEmail(
        [FromBody] EmailDto dto,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ResendConfirmationEmailRequest(
                dto.Email),
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
        [FromBody] EmailDto dto,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ForgotPasswordRequest(dto.Email),
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
        [FromBody] ResetPasswordDto dto,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ResetPasswordRequest(
                dto.Email,
                dto.ResetCode,
                dto.NewPassword),
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
        return result.Success
            ? Ok(result.Value)
            : NotFound(result);
    }

    /// <summary>Updates authenticated identity information.</summary>
    [HttpPost("manage/info")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UpdateInfo(
        [FromBody] InfoDto dto,
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
                dto.NewEmail,
                dto.NewPassword,
                dto.OldPassword),
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
        [FromBody] TwoFactorDto dto,
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
                    dto.Enable,
                    dto.TwoFactorCode,
                    dto.ResetRecoveryCodes,
                    dto.ResetSharedKey,
                    dto.ForgetMachine),
                cancellationToken);
        return result.Success
            ? Ok(result.Value)
            : BadRequest(result);
    }

    private string? GetUserId()
    {
        return User.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
