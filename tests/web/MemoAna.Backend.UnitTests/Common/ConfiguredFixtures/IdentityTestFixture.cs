using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Identity.Options;
using MemoAna.Backend.Infrastructure.Identity.Services;
using MemoAna.Backend.Infrastructure.Persistence;
using MemoAna.Backend.UnitTests.Common.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace MemoAna.Backend.UnitTests.Common.ConfiguredFixtures;

/// <summary>Builds a fully configured in-memory Identity fixture.</summary>
public sealed class IdentityTestFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    internal CapturingEmailSender EmailSender { get; }

    /// <summary>Initializes the Identity test fixture.</summary>
    public IdentityTestFixture()
    {
        EmailSender = new CapturingEmailSender();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddHttpContextAccessor();
        _ = services.AddAuthentication(options => options.DefaultScheme = IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme);
        _ = services.AddDbContext<MemoAnaDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        _ = services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<Role>()
            .AddSignInManager<SignInManager<User>>()
            .AddEntityFrameworkStores<MemoAnaDbContext>()
            .AddDefaultTokenProviders();
        _ = services.Configure<JwtOptions>(options =>
        {
            options.Key = "MemoAna.Backend-test-secret-key-with-at-least-256-bits-2026";
            options.Issuer = "MemoAna.Backend.Test";
            options.Audience = "MemoAna.Backend.Test";
            options.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            options.RefreshTokenLifetime = TimeSpan.FromDays(14);
        });
        _ = services.AddSingleton<IRevokedTokenStore, RevokedTokenStore>();
        _ = services.AddScoped<IJwtTokenService, JwtTokenService>();
        _ = services.AddSingleton<IIdentityEmailSender>(EmailSender);
        _ = services.AddScoped<IdentityService>();
        
        _provider = services.BuildServiceProvider();
        _ = _provider.GetRequiredService<MemoAnaDbContext>().Database.EnsureCreated();
    }
        
    public IdentityService Service => _provider.GetRequiredService<IdentityService>();
    public IJwtTokenService TokenService => _provider.GetRequiredService<IJwtTokenService>();
    public UserManager<User> UserManager => _provider.GetRequiredService<UserManager<User>>();
    public RoleManager<Role> RoleManager => _provider.GetRequiredService<RoleManager<Role>>();

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

    /// <summary>
    /// Generates a TOTP from the user's authenticator key.
    /// </summary>
    /// <param name="user">The user for whom the code is generated.</param>
    /// <returns>A code accepted by Identity's authenticator provider.</returns>
    public async Task<string> GenerateValidAuthenticatorCodeAsync(User user)
    {
        string? key = await UserManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            IdentityResult result =
                await UserManager.ResetAuthenticatorKeyAsync(user);
            Assert.True(result.Succeeded);
            key = await UserManager.GetAuthenticatorKeyAsync(user);
        }

        Assert.False(string.IsNullOrWhiteSpace(key));
        byte[] secret = DecodeBase32(key!);
        long timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        byte[] counter = new byte[8];
        for (int index = 7; index >= 0; index--)
        {
            counter[index] = (byte)(timestep & 0xff);
            timestep >>= 8;
        }

        using HMACSHA1 hmac = new(secret);
        byte[] hash = hmac.ComputeHash(counter);
        int offset = hash[^1] & 0x0f;
        int binaryCode =
            ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0;
        int bits = 0;
        foreach (char character in value.Trim().ToUpperInvariant())
        {
            int digit = alphabet.IndexOf(character);
            if (digit < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits < 8)
            {
                continue;
            }

            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        return [.. bytes];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
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
