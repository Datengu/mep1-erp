using Mep1.Erp.Core;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Mep1.Erp.Api.Security;

public sealed class JwtTokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SymmetricSecurityKey _key;
    private readonly int _expiryMinutes;

    public JwtTokenService(IConfiguration config)
    {
        var signingKey = config["Security:JwtSigningKey"] ?? "";
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("Security:JwtSigningKey is missing.");

        _issuer = config["Security:JwtIssuer"] ?? "mep1-erp";
        _audience = config["Security:JwtAudience"] ?? "mep1-erp";

        if (!int.TryParse(config["Security:JwtExpiryMinutes"], out _expiryMinutes))
            _expiryMinutes = 720; // 12h default

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        if (_key.KeySize < 256)
            throw new InvalidOperationException("JwtSigningKey must be at least 32 bytes (256 bits).");
    }

    public (string token, DateTime expiresUtc) CreateToken(int workerId, TimesheetUserRole role, string username)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_expiryMinutes);

        var claims = new List<Claim>
        {
            // Standard claims
            new Claim(ClaimTypes.NameIdentifier, workerId.ToString()),
            new Claim(ClaimTypes.Name, username ?? ""),
            new Claim(ClaimTypes.Role, role.ToString()),

            // Explicit app claims (nice for debugging / future)
            new Claim("wid", workerId.ToString()),
            new Claim("usr", username ?? ""),
            new Claim("role", role.ToString()),
        };

        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, expires);
    }
}
