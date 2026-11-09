using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using HotelManagement.Application.Auth;
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

    /// <summary>The user's security stamp at issue time; a mismatch later means the session was revoked.</summary>
    public const string SecurityStamp = "stamp";
}

public class JwtOptions
{
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";

    /// <summary>HMAC-SHA256 signing key; at least 32 characters.</summary>
    [Required, MinLength(32)] public string Key { get; set; } = "";

    /// <summary>Access-token lifetime. Kept short; clients renew with a refresh token.</summary>
    [Range(1, 24 * 60)] public int ExpiryMinutes { get; set; } = 15;

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
                [AppClaims.SecurityStamp] = user.SecurityStamp,
            },
        };
        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}

/// <summary>
/// Adapts ASP.NET Core Identity's hasher: PBKDF2-HMAC-SHA512, 100,000 iterations and a random
/// 128-bit salt per hash, all encoded in the stored string. Reports when a stored hash used
/// weaker settings, so callers can upgrade it.
/// </summary>
internal class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly object NoUser = new();
    private readonly PasswordHasher<object> _hasher = new();

    /// <summary>A real hash of a random value: verifying against it costs the same as a real check.</summary>
    private readonly Lazy<string> _dummyHash;

    public IdentityPasswordHasher() =>
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(NoUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public string Hash(string password) => _hasher.HashPassword(NoUser, password);

    public PasswordCheck Verify(string? passwordHash, string password)
    {
        if (passwordHash is null)
        {
            _hasher.VerifyHashedPassword(NoUser, _dummyHash.Value, password ?? "");
            return PasswordCheck.Failed;
        }

        return _hasher.VerifyHashedPassword(NoUser, passwordHash, password ?? "") switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
    }
}

/// <summary>256-bit random refresh tokens (URL-safe base64) and their SHA-256 hash.</summary>
internal sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    public string NewToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
