using System.ComponentModel.DataAnnotations;
using System.Text;
using HotelManagement.Application.Common;
using HotelManagement.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HotelManagement.Infrastructure.Security;

/// <summary>Claim names in issued tokens. Shared with the web layer that validates them.</summary>
public static class AppClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string CompanyId = "company_id";
}

public class JwtOptions
{
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";

    /// <summary>HMAC-SHA256 signing key; at least 32 characters.</summary>
    [Required, MinLength(32)] public string Key { get; set; } = "";

    [Range(1, 24 * 60)] public int ExpiryMinutes { get; set; } = 60;

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));
}

internal class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateToken(User user)
    {
        var jwt = options.Value;
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(jwt.ExpiryMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(jwt.SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [AppClaims.Subject] = user.Id.ToString(),
                [AppClaims.Email] = user.Email,
                [AppClaims.Name] = user.FullName,
                [AppClaims.Role] = user.Role.ToString(),
                [AppClaims.CompanyId] = user.CompanyId.ToString(),
            },
        };
        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}

/// <summary>Adapts ASP.NET Core Identity's PBKDF2 hasher to the application's abstraction.</summary>
internal class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly object NoUser = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(NoUser, password);

    public bool Verify(string passwordHash, string password) =>
        _hasher.VerifyHashedPassword(NoUser, passwordHash, password) != PasswordVerificationResult.Failed;
}
