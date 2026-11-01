using MemoAna.Auth.Abstract;
using MemoAna.Auth.Responses;
using MemoAna.Server.Infrastructure.Auth.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Management;
using System.Security.Claims;
using System.Text;

namespace MemoAna.Server.Infrastructure.Services.Concrete;

/// <summary>Creates and validates signed JWT access and refresh tokens.</summary>
/// <param name="options">The configured JWT options.</param>
/// <param name="revokedTokens">The store used to reject revoked tokens.</param>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IRevokedTokenStore revokedTokens) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JwtSecurityTokenHandler tokenHandler = new();
    /// <inheritdoc />
    public TokenResponse CreateTokens(string userId, string email, IEnumerable<string> roles, IEnumerable<Claim> claims)
    {
        DateTime now = DateTime.UtcNow;
        JwtSecurityToken access = CreateToken(
            userId,
            email,
            roles,
            claims,
            JwtTokenTypes.Access,
            now,
            _options.AccessTokenLifetime);
        JwtSecurityToken refresh = CreateToken(
            userId,
            email,
            roles,
            [],
            JwtTokenTypes.Refresh,
            now,
            _options.RefreshTokenLifetime);

        return new TokenResponse(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(access),
            (int)_options.AccessTokenLifetime.TotalSeconds,
            new JwtSecurityTokenHandler().WriteToken(refresh));
    }

    /// <inheritdoc />
    public ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateSecurityKey(),
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            ClaimsPrincipal principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out SecurityToken? validatedToken);
            var tokenId = validatedToken.Id;
            if (!string.IsNullOrWhiteSpace(tokenId) && revokedTokens.IsRevoked(tokenId))
            {
                return null;
            }

            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? GetTokenId(string token)
    {
        try
        {
            return tokenHandler.ReadJwtToken(token).Id;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public DateTime? GetExpiration(string token)
    {
        try
        {
            JwtSecurityToken jwt = tokenHandler.ReadJwtToken(token);
            return jwt.ValidTo == DateTime.MinValue ? null : jwt.ValidTo;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private JwtSecurityToken CreateToken(
        string userId,
        string email,
        IEnumerable<string> roles,
        IEnumerable<Claim> claims,
        string tokenType,
        DateTime issuedAt,
        TimeSpan lifetime)
    {
        var tokenClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("N")),
            new("token_type", tokenType)
        };

        tokenClaims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        tokenClaims.AddRange(claims);

        var credentials = new SigningCredentials(CreateSecurityKey(), SecurityAlgorithms.HmacSha256);

        return new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: tokenClaims,
            notBefore: issuedAt.ToUniversalTime(),
            expires: issuedAt.Add(lifetime).ToUniversalTime(),
            signingCredentials: credentials);
    }

    private SymmetricSecurityKey CreateSecurityKey()
    {
        var bytes = Encoding.UTF8.GetBytes(_options.Key);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must contain at least 256 bits.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}

/// <summary>Stores revoked token identifiers in process until their natural expiration.</summary>
public sealed class RevokedTokenStore : IRevokedTokenStore
{
    private readonly ConcurrentDictionary<string, DateTime> _tokens = new();

    /// <inheritdoc />
    public bool IsRevoked(string tokenId)
    {
        if (!_tokens.TryGetValue(tokenId, out DateTime expiresAt))
        {
            return false;
        }

        if (expiresAt > DateTime.UtcNow)
        {
            return true;
        }

        _ = _tokens.TryRemove(tokenId, out _);
        return false;
    }

    /// <inheritdoc />
    public void Revoke(string tokenId, DateTime expiresAt)
    {
        if (expiresAt > DateTime.UtcNow)
        {
            _tokens[tokenId] = expiresAt;
        }
    }
}
