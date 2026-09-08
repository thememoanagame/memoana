using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Identity.Options;
using MemoAna.Backend.Infrastructure.Identity.Services;
using MemoAna.Backend.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemoAna.Backend.UnitTests.Common.Fixtures;

/// <summary>Builds an in-memory Identity service.</summary>
public sealed class IdentityTestFixture : IDisposable
{
    private readonly ServiceProvider provider;

    /// <summary>Initializes the Identity test fixture.</summary>
    public IdentityTestFixture()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddHttpContextAccessor();
        _ = services.AddDbContext<MemoAnaDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _ = services.AddIdentityCore<User>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 4;
            options.SignIn.RequireConfirmedEmail = false;
        }).AddRoles<Role>()
        .AddEntityFrameworkStores<MemoAnaDbContext>()
        .AddSignInManager();
        _ = services.AddScoped<IIdentityEmailSender,
            TestIdentityEmailSender>();
        _ = services.Configure<
            JwtOptions>(
            options =>
            {
                options.Key =
                    "01234567890123456789012345678901";
                options.Issuer = "MemoAna.Backend.Tests";
                options.Audience = "MemoAna.Backend.Tests";
            });
        _ = services.AddSingleton<IRevokedTokenStore,
            RevokedTokenStore>();
        _ = services.AddScoped<IJwtTokenService,
            JwtTokenService>();
        _ = services.AddScoped<IdentityService>();
        provider = services.BuildServiceProvider();
    }

    /// <summary>Gets the Identity service under test.</summary>
    public IdentityService Service =>
        provider.GetRequiredService<IdentityService>();

    /// <summary>Gets the database context.</summary>
    public MemoAnaDbContext DbContext =>
        provider.GetRequiredService<MemoAnaDbContext>();

    /// <summary>Gets the user manager.</summary>
    public UserManager<User> UserManager =>
        provider.GetRequiredService<UserManager<User>>();

    /// <summary>Gets the role manager.</summary>
    public RoleManager<Role> RoleManager =>
        provider.GetRequiredService<RoleManager<Role>>();

    /// <summary>Creates and stores a user.</summary>
    /// <param name="email">The user's email.</param>
    /// <param name="password">The user's password.</param>
    /// <returns>The created user.</returns>
    public async Task<User> CreateUserAsync(
        string email,
        string password = "Password1!")
    {
        User user = new(email)
        {
            Email = email,
            EmailConfirmed = true
        };
        IdentityResult result = await UserManager.CreateAsync(
            user,
            password);
        Assert.True(result.Succeeded);
        return user;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        provider.Dispose();
    }

    private sealed class TestIdentityEmailSender :
        IIdentityEmailSender
    {
        public Task SendConfirmationAsync(
            string email,
            string confirmationLink,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(
            string email,
            string resetLink,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
