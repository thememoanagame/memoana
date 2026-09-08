using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MemoAna.Backend.Application.Identity.Responses;
using MemoAna.Backend.Infrastructure.Identity.Options;
using MemoAna.Backend.Infrastructure.Identity.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MemoAna.Backend.UnitTests.Identity;

/// <summary>Tests JWT token creation and validation.</summary>
public sealed class JwtTokenServiceTests
{
    private const string Key =
        "01234567890123456789012345678901";

    private static JwtTokenService Create(
        RevokedTokenStore? store = null)
    {
        JwtOptions options = new()
        {
            Key = Key,
            Issuer = "MemoAna.Backend.Tests",
            Audience = "MemoAna.Backend.Tests"
        };

        return new JwtTokenService(
            Options.Create(options),
            store ?? new RevokedTokenStore());
    }

    [Fact]
    public void CreateTokens_CreatesExpectedClaims()
    {
        JwtTokenService service = Create();
        TokenResponse result = service.CreateTokens(
            "user-1",
            "user@example.com",
            ["User", "Admin"],
            [new Claim("permission", "read")]);

        Assert.Equal("Bearer", result.TokenType);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);

        ClaimsPrincipal? principal = service.ValidateToken(
            result.AccessToken);

        Assert.NotNull(principal);
        Assert.Equal("user-1",
            principal.FindFirstValue(
                ClaimTypes.NameIdentifier));
        Assert.Equal("user@example.com",
            principal.FindFirstValue(ClaimTypes.Email));
        Assert.Equal("access",
            principal.FindFirstValue("token_type"));
        Assert.Equal("read",
            principal.FindFirstValue("permission"));
        Assert.Equal(2,
            principal.FindAll(ClaimTypes.Role).Count());
    }

    [Fact]
    public void RefreshToken_HasRefreshType()
    {
        JwtTokenService service = Create();
        TokenResponse result = service.CreateTokens(
            "user-1",
            "user@example.com",
            [],
            []);

        ClaimsPrincipal? principal = service.ValidateToken(
            result.RefreshToken);

        Assert.NotNull(principal);
        Assert.Equal("refresh",
            principal.FindFirstValue("token_type"));
    }

    [Fact]
    public void ValidateToken_EmptyToken_ReturnsNull()
    {
        JwtTokenService service = Create();

        Assert.Null(service.ValidateToken(string.Empty));
        Assert.Null(service.ValidateToken(" "));
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsNull()
    {
        Assert.Null(Create().ValidateToken("invalid-token"));
    }

    [Fact]
    public void ValidateToken_RevokedToken_ReturnsNull()
    {
        RevokedTokenStore store = new();
        JwtTokenService service = Create(store);
        TokenResponse tokens = service.CreateTokens(
            "user-1", "user@example.com", [], []);
        string tokenId = service.GetTokenId(
            tokens.AccessToken)!;
        DateTimeOffset expiration = service.GetExpiration(
            tokens.AccessToken)!.Value;

        store.Revoke(tokenId, expiration);

        Assert.Null(service.ValidateToken(
            tokens.AccessToken));
    }

    [Fact]
    public void ValidateToken_ExpiredWithoutLifetime_ReturnsPrincipal()
    {
        JwtTokenService service = Create();
        string token = CreateExpiredToken();

        ClaimsPrincipal? principal = service.ValidateToken(
            token, false);

        Assert.NotNull(principal);
    }

    [Fact]
    public void ValidateToken_ExpiredWithLifetime_ReturnsNull()
    {
        JwtTokenService service = Create();
        string token = CreateExpiredToken();

        Assert.Null(service.ValidateToken(token));
    }

    [Fact]
    public void GetTokenId_InvalidToken_ReturnsNull()
    {
        Assert.Null(Create().GetTokenId("invalid"));
    }

    [Fact]
    public void GetTokenId_ValidToken_ReturnsIdentifier()
    {
        JwtTokenService service = Create();
        TokenResponse tokens = service.CreateTokens(
            "user-1", "user@example.com", [], []);

        Assert.False(string.IsNullOrWhiteSpace(
            service.GetTokenId(tokens.AccessToken)));
    }

    [Fact]
    public void GetExpiration_InvalidToken_ReturnsNull()
    {
        Assert.Null(Create().GetExpiration("invalid"));
    }

    [Fact]
    public void GetExpiration_ValidToken_ReturnsExpiration()
    {
        JwtTokenService service = Create();
        TokenResponse tokens = service.CreateTokens(
            "user-1", "user@example.com", [], []);

        DateTimeOffset? expiration = service.GetExpiration(
            tokens.AccessToken);

        _ = Assert.NotNull(expiration);
        Assert.True(expiration > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void CreateTokens_ShortKey_Throws()
    {
        JwtOptions options = new()
        {
            Key = "short",
            Issuer = "MemoAna.Backend.Tests",
            Audience = "MemoAna.Backend.Tests"
        };
        JwtTokenService service = new(
            Options.Create(options),
            new RevokedTokenStore());

        _ = Assert.Throws<InvalidOperationException>(() =>
            service.CreateTokens(
                "user-1", "user@example.com", [], []));
    }

    private static string CreateExpiredToken()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SymmetricSecurityKey key = new(
            Encoding.UTF8.GetBytes(Key));
        SigningCredentials credentials = new(
            key, SecurityAlgorithms.HmacSha256);
        JwtSecurityToken token = new(
            "MemoAna.Backend.Tests",
            "MemoAna.Backend.Tests",
            [new Claim(
                JwtRegisteredClaimNames.Sub, "user-1")],
            now.AddMinutes(-2).UtcDateTime,
            now.AddMinutes(-1).UtcDateTime,
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
