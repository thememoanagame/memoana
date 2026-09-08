using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Identity.Commands;
using MemoAna.Backend.Application.Identity.Queries;
using MemoAna.Backend.Application.Identity.Requests;
using MemoAna.Backend.Application.Identity.Responses;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MemoAna.Backend.Controllers.v1;

/// <summary>Exposes identity endpoints.</summary>
/// <param name="mediator">The application mediator.</param>
[ApiController]
[Route("api/v1/identity")]
public sealed class IdentityController(
    IMediator mediator) : ControllerBase
{
    /// <summary>Registers a new user.</summary>
    [HttpPost("/register")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new RegisterCommand(
                request.Email,
                request.Password),
            cancellationToken);
        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Authenticates a user.</summary>
    [HttpPost("/login")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<Response<TokenResponse>>(401)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        Response<TokenResponse> result =
            await mediator.Send(
                new LoginCommand(
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
    [HttpPost("/login/google-play-games")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<Response<TokenResponse>>(401)]
    public async Task<IActionResult> GooglePlayGamesLogin(
        GooglePlayGamesLoginRequest request,
        CancellationToken cancellationToken)
    {
        Response<TokenResponse> result =
            await mediator.Send(
                new GooglePlayGamesLoginCommand(
                    request.ServerAuthCode),
                cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : Unauthorized(result);
    }

    /// <summary>Refreshes authentication tokens.</summary>
    [HttpPost("/refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<Response<TokenResponse>>(401)]
    public async Task<IActionResult> Refresh(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        Response<TokenResponse> result = await mediator.Send(
            new RefreshTokenCommand(request.RefreshToken),
            cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : Unauthorized(result);
    }

    /// <summary>Revokes the current access token.</summary>
    [HttpPost("/revoke")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType<Response<bool>>(401)]
    public async Task<IActionResult> Revoke(
        CancellationToken cancellationToken)
    {
        string token = Request.Headers.Authorization
            .ToString()
            .Replace(
                "Bearer ",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);

        Response<bool> result = await mediator.Send(
            new RevokeTokenCommand(token),
            cancellationToken);

        return result.Data == true
            ? Ok()
            : Unauthorized(result);
    }

    /// <summary>Confirms a user's email.</summary>
    [HttpGet("/confirmEmail")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<Response<bool>>(400)]
    public async Task<IActionResult> ConfirmEmail(
        [FromQuery] string userId,
        [FromQuery] string code,
        [FromQuery] string? changedEmail,
        CancellationToken cancellationToken)
    {
        Response<bool> result = await mediator.Send(
            new ConfirmEmailCommand(
                userId,
                code,
                changedEmail),
            cancellationToken);
        return result.Data == true
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Resends the confirmation email.</summary>
    [HttpPost("/resendConfirmationEmail")]
    [AllowAnonymous]
    [ProducesResponseType<IdentityResultResponse>(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> ResendConfirmationEmail(
        EmailRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ResendConfirmationEmailCommand(
                request.Email),
            cancellationToken);
        return result.Succeeded
            ? Ok(result)
            : BadRequest(result);
    }

    /// <summary>Starts password recovery.</summary>
    [HttpPost("/forgotPassword")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> ForgotPassword(
        EmailRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ForgotPasswordCommand(request.Email),
            cancellationToken);
        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Resets a password.</summary>
    [HttpPost("/resetPassword")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType<IdentityResultResponse>(400)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        IdentityResultResponse result = await mediator.Send(
            new ResetPasswordCommand(
                request.Email,
                request.ResetCode,
                request.NewPassword),
            cancellationToken);
        return result.Succeeded
            ? Ok()
            : BadRequest(result);
    }

    /// <summary>Gets authenticated identity information.</summary>
    [HttpGet("/manage/info")]
    [Authorize]
    [ProducesResponseType<IdentityInfoResponse>(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType<Response<IdentityInfoResponse>>(404)]
    public async Task<IActionResult> GetInfo(
        CancellationToken cancellationToken)
    {
        string? userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        Response<IdentityInfoResponse> result =
            await mediator.Send(
                new GetIdentityInfoQuery(userId),
                cancellationToken);
        return result.Succeeded
            ? Ok(result.Data)
            : NotFound(result);
    }

    /// <summary>Updates authenticated identity information.</summary>
    [HttpPost("/manage/info")]
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
            new UpdateIdentityInfoCommand(
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
    [HttpPost("/manage/2fa")]
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

        Response<TwoFactorResponse> result =
            await mediator.Send(
                new ConfigureTwoFactorCommand(
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
