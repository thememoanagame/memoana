using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MemoAna.Backend.Application.Common.Responses;
using MemoAna.Backend.Application.Identity.Commands;
using MemoAna.Backend.Application.Identity.Responses;
using MemoAna.Backend.Application.Identity.Requests;
using MemoAna.Backend.Application.Identity.Queries;
using MemoAna.Backend.Controllers.v1;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MemoAna.Backend.UnitTests.Presentation;

/// <summary>Tests identity controller dispatch and mapping.</summary>
public sealed class IdentityControllerTests
{
    [Fact]
    public async Task Register_Success_ReturnsOk()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<RegisterCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Success()));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.Register(
            new RegisterRequest("a@b.com", "Password1!"),
            CancellationToken.None);

        _ = Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Register_Failure_ReturnsBadRequest()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<RegisterCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Failure(["error"])));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.Register(
            new RegisterRequest("a@b.com", "Password1!"),
            CancellationToken.None);

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_Success_ReturnsToken()
    {
        Mock<IMediator> mediator = new();
        TokenResponse token = new("Bearer", "access", 300, "refresh");
        _ = mediator.Setup(item => item.Send(
                It.IsAny<LoginCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<TokenResponse>>(
                Response.Success(token)));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.Login(
            new LoginRequest(
                "a@b.com",
                "Password1!",
                "123456",
                null),
            CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(token, ok.Value);
    }

    [Fact]
    public async Task Login_Failure_ReturnsUnauthorized()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<LoginCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<TokenResponse>>(
                Response.Failure<TokenResponse>("error")));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.Login(
            new LoginRequest("a@b.com", "bad", null, null),
            CancellationToken.None);

        _ = Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Refresh_Success_ReturnsToken()
    {
        Mock<IMediator> mediator = new();
        TokenResponse token = new("Bearer", "access", 300, "refresh");
        _ = mediator.Setup(item => item.Send(
                It.IsAny<RefreshTokenCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<TokenResponse>>(
                Response.Success(token)));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.Refresh(
            new RefreshRequest("refresh"),
            CancellationToken.None);

        _ = Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Refresh_Failure_ReturnsUnauthorized()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<RefreshTokenCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<TokenResponse>>(
                Response.Failure<TokenResponse>("error")));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.Refresh(
            new RefreshRequest("refresh"),
            CancellationToken.None);

        _ = Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Revoke_True_ReturnsOk()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<RevokeTokenCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<bool>>(
                Response.Success(true)));
        IdentityController controller = CreateController(mediator);
        controller.Request.Headers.Authorization = "Bearer token";

        IActionResult result = await controller.Revoke(
            CancellationToken.None);

        _ = Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Revoke_False_ReturnsUnauthorized()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<RevokeTokenCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<bool>>(
                Response.Success(false)));
        IdentityController controller = CreateController(mediator);

        IActionResult result = await controller.Revoke(
            CancellationToken.None);

        _ = Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task ConfirmEmail_True_ReturnsOk()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ConfirmEmailCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<bool>>(
                Response.Success(true)));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ConfirmEmail(
            "user",
            "code",
            null,
            CancellationToken.None);

        _ = Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ConfirmEmail_False_ReturnsBadRequest()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ConfirmEmailCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<bool>>(
                Response.Success(false)));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ConfirmEmail(
            "user",
            "code",
            "new@b.com",
            CancellationToken.None);

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResendConfirmation_Success_ReturnsOk()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ResendConfirmationEmailCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Success()));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ResendConfirmationEmail(
            new EmailRequest("a@b.com"),
            CancellationToken.None);

        _ = Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ResendConfirmation_Failure_ReturnsBadRequest()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ResendConfirmationEmailCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Failure(["error"])));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ResendConfirmationEmail(
            new EmailRequest("a@b.com"),
            CancellationToken.None);

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ForgotPassword_Success_ReturnsOk()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ForgotPasswordCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Success()));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ForgotPassword(
            new EmailRequest("a@b.com"),
            CancellationToken.None);

        _ = Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ForgotPassword_Failure_ReturnsBadRequest()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ForgotPasswordCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Failure(["error"])));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ForgotPassword(
            new EmailRequest("a@b.com"),
            CancellationToken.None);

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResetPassword_Success_ReturnsOk()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ResetPasswordCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Success()));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ResetPassword(
            new ResetPasswordRequest(
                "a@b.com",
                "code",
                "Password2!"),
            CancellationToken.None);

        _ = Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ResetPassword_Failure_ReturnsBadRequest()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ResetPasswordCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Failure(["error"])));
        IdentityController controller = new(mediator.Object);

        IActionResult result = await controller.ResetPassword(
            new ResetPasswordRequest("a@b.com", "code", "bad"),
            CancellationToken.None);

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetInfo_MissingIdentity_ReturnsUnauthorized()
    {
        IdentityController controller = CreateController(
            new Mock<IMediator>());

        IActionResult result = await controller.GetInfo(
            CancellationToken.None);

        _ = Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetInfo_Success_UsesSubjectClaim()
    {
        Mock<IMediator> mediator = new();
        IdentityInfoResponse info = new("a@b.com", true);
        _ = mediator.Setup(item => item.Send(
                It.IsAny<GetIdentityInfoQuery>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<IdentityInfoResponse>>(
                Response.Success(info)));
        IdentityController controller = CreateController(
            mediator,
            new Claim(JwtRegisteredClaimNames.Sub, "subject"));

        IActionResult result = await controller.GetInfo(
            CancellationToken.None);

        _ = Assert.IsType<OkObjectResult>(result);
        mediator.Verify(item => item.Send(
                It.Is<GetIdentityInfoQuery>(query =>
                    query.UserId == "subject"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetInfo_MissingUser_ReturnsNotFound()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<GetIdentityInfoQuery>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<IdentityInfoResponse>>(
                Response.Failure<IdentityInfoResponse>("missing")));
        IdentityController controller = CreateController(
            mediator,
            new Claim(ClaimTypes.NameIdentifier, "user"));

        IActionResult result = await controller.GetInfo(
            CancellationToken.None);

        _ = Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateInfo_MissingIdentity_ReturnsUnauthorized()
    {
        IdentityController controller = CreateController(
            new Mock<IMediator>());

        IActionResult result = await controller.UpdateInfo(
            new InfoRequest("new@b.com", null, "old"),
            CancellationToken.None);

        _ = Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateInfo_Success_ReturnsOk()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<UpdateIdentityInfoCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Success()));
        IdentityController controller = CreateController(
            mediator,
            new Claim(ClaimTypes.NameIdentifier, "user"));

        IActionResult result = await controller.UpdateInfo(
            new InfoRequest("new@b.com", "Password2!", "Password1!"),
            CancellationToken.None);

        _ = Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task UpdateInfo_Failure_ReturnsBadRequest()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<UpdateIdentityInfoCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IdentityResultResponse>(
                IdentityResultResponse.Failure(["error"])));
        IdentityController controller = CreateController(
            mediator,
            new Claim(ClaimTypes.NameIdentifier, "user"));

        IActionResult result = await controller.UpdateInfo(
            new InfoRequest("new@b.com", "Password2!", "Password1!"),
            CancellationToken.None);

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ConfigureTwoFactor_MissingIdentity_ReturnsUnauthorized()
    {
        IdentityController controller = CreateController(
            new Mock<IMediator>());

        IActionResult result = await controller.ConfigureTwoFactor(
            new TwoFactorRequest(true, "123456", false, false, false),
            CancellationToken.None);

        _ = Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ConfigureTwoFactor_Success_ReturnsResponse()
    {
        Mock<IMediator> mediator = new();
        TwoFactorResponse response = new(
            "KEY",
            5,
            ["code"],
            true,
            false);
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ConfigureTwoFactorCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<TwoFactorResponse>>(
                Response.Success(response)));
        IdentityController controller = CreateController(
            mediator,
            new Claim(ClaimTypes.NameIdentifier, "user"));

        IActionResult result = await controller.ConfigureTwoFactor(
            new TwoFactorRequest(true, "123456", false, false, false),
            CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task ConfigureTwoFactor_Failure_ReturnsBadRequest()
    {
        Mock<IMediator> mediator = new();
        _ = mediator.Setup(item => item.Send(
                It.IsAny<ConfigureTwoFactorCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Response<TwoFactorResponse>>(
                Response.Failure<TwoFactorResponse>("error")));
        IdentityController controller = CreateController(
            mediator,
            new Claim(ClaimTypes.NameIdentifier, "user"));

        IActionResult result = await controller.ConfigureTwoFactor(
            new TwoFactorRequest(true, "123456", false, false, false),
            CancellationToken.None);

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    private static IdentityController CreateController(
        Mock<IMediator> mediator,
        Claim? claim = null)
    {
        IdentityController controller = new(mediator.Object);
        ClaimsIdentity identity = new();
        if (claim is not null)
        {
            identity.AddClaim(claim);
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
        return controller;
    }
}
