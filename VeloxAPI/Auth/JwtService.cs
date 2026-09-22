using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Velox.Shared.Models;

namespace VeloxAPI.Auth;

/// <summary>
/// Issues and validates HMAC-SHA256 JWTs for VeloxLink employee authentication.
/// Secret is read from VELOX_JWT_SECRET env var.
/// </summary>
public sealed class JwtService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);
    private readonly string _secret;

    public JwtService(IConfiguration config)
    {
        _secret = config["VELOX_JWT_SECRET"]
               ?? Environment.GetEnvironmentVariable("VELOX_JWT_SECRET")
               ?? throw new InvalidOperationException("VELOX_JWT_SECRET is not configured.");
    }

    public (string Token, DateTimeOffset ExpiresAt) IssueToken(string employeeId, string name, string tenantId, EmployeeRole role)
    {
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.Add(TokenLifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,  employeeId),
            new Claim(JwtRegisteredClaimNames.Name, name),
            new Claim("tid",  tenantId),
            new Claim("role", role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer:   "velox-api",
            audience: "velox-clients",
            claims:   claims,
            expires:  expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        try
        {
            var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var handler = new JwtSecurityTokenHandler();
            handler.InboundClaimTypeMap.Clear();

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer           = false,
                ValidateAudience         = false,
                ValidateLifetime         = true,
                IssuerSigningKey         = key,
                ValidateIssuerSigningKey = true,
                ClockSkew                = TimeSpan.FromSeconds(60)
            }, out _);
            return principal;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JwtService] Token validation failed: {ex.Message}");
            return null;
        }
    }
}
