using MemoAna.Backend.Infrastructure.Identity.Options;
using MemoAna.Backend.Infrastructure.Identity.Services;
using Microsoft.Extensions.Logging;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests supporting Identity infrastructure branches.</summary>
public sealed class IdentityInfrastructureBranchTests
{
    [Fact]
    public async Task LoggingEmailSender_LogsBothMessageKinds()
    {
        using ILoggerFactory factory = LoggerFactory.Create(
            builder => builder.AddDebug());
        ILogger<LoggingIdentityEmailSender> logger =
            factory.CreateLogger<LoggingIdentityEmailSender>();
        LoggingIdentityEmailSender sender = new(logger);

        await sender.SendConfirmationAsync(
            "user@example.com", "/confirm",
            CancellationToken.None);
        await sender.SendPasswordResetAsync(
            "user@example.com", "/reset",
            CancellationToken.None);
    }

    [Fact]
    public void JwtOptions_DefaultsAreConfigured()
    {
        JwtOptions options =
            new()
            {
                Key = "01234567890123456789012345678901"
            };

        Assert.Equal("MemoAna.Backend", options.Issuer);
        Assert.Equal("MemoAna.Backend", options.Audience);
        Assert.Equal(TimeSpan.FromMinutes(15),
            options.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(14),
            options.RefreshTokenLifetime);
    }
}
