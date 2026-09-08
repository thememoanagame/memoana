using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MemoAna.Backend.Application.Common.Contracts;
using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Application.Identity.Responses;
using MemoAna.Backend.Infrastructure.Identity.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MemoAna.Backend.Infrastructure.Identity.Services;

/// <summary>Creates and validates signed JWT access and refresh tokens.</summary>
/// <param name="options">The configured JWT options.</param>
/// <param name="revokedTokens">The store used to reject revoked tokens.</param>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IRevokedTokenStore revokedTokens) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    /// <inheritdoc />
    public TokenResponse CreateTokens(string userId, string email, IEnumerable<string> roles, IEnumerable<Claim> claims)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
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
            return new JwtSecurityTokenHandler().ReadJwtToken(token).Id;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? GetExpiration(string token)
    {
        try
        {
            JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo == DateTime.MinValue ? null : new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
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
        DateTimeOffset issuedAt,
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
            notBefore: issuedAt.UtcDateTime,
            expires: issuedAt.Add(lifetime).UtcDateTime,
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
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();

    /// <inheritdoc />
    public bool IsRevoked(string tokenId)
    {
        if (!_tokens.TryGetValue(tokenId, out DateTimeOffset expiresAt))
        {
            return false;
        }

        if (expiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _ = _tokens.TryRemove(tokenId, out _);
        return false;
    }

    /// <inheritdoc />
    public void Revoke(string tokenId, DateTimeOffset expiresAt)
    {
        if (expiresAt > DateTimeOffset.UtcNow)
        {
            _tokens[tokenId] = expiresAt;
        }
    }
}
