using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Users;
using HotelManagement.Domain;
using Microsoft.Extensions.Options;

namespace HotelManagement.Application.Auth;

public interface IRefreshTokenRepository
{
    /// <summary>Across all companies: refresh happens before the tenant is known.</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);

    Task<IReadOnlyList<RefreshToken>> ListActiveInFamilyAsync(Guid familyId, CancellationToken ct);
    Task<IReadOnlyList<RefreshToken>> ListActiveForUserAsync(Guid userId, CancellationToken ct);
    void Add(RefreshToken token);
}

/// <summary>High-entropy opaque tokens (refresh tokens) and their fast, non-reversible hash.</summary>
public interface ISecureTokenGenerator
{
    string NewToken();

    /// <summary>SHA-256: fine for 256-bit random tokens (unlike passwords, they can't be guessed).</summary>
    string Hash(string token);
}

public class RefreshTokenOptions
{
    /// <summary>Refresh tokens expire after this many days without use.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);
}

/// <summary>Issues, rotates and revokes login sessions (access token + refresh token pairs).</summary>
public interface IAuthSessionService
{
    /// <summary>Starts a new session for an authenticated user. The caller saves the unit of work.</summary>
    AuthResponse StartSession(User user);

    /// <summary>Exchanges a refresh token for a new pair, rotating the refresh token.</summary>
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct);

    /// <summary>Ends the session the refresh token belongs to.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct);

    /// <summary>Revokes every session of a user (e.g. on deactivation). The caller saves.</summary>
    Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct);
}

public class AuthSessionService(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    ISecureTokenGenerator tokenGenerator,
    ITokenService accessTokens,
    IUnitOfWork unitOfWork,
    IOptions<RefreshTokenOptions> options,
    TimeProvider clock) : IAuthSessionService
{
    private const string InvalidSession = "The session has expired or is no longer valid. Please log in again.";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public AuthResponse StartSession(User user) => Issue(user, familyId: Guid.NewGuid(), out _);

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var stored = await refreshTokens.FindByHashAsync(tokenGenerator.Hash(refreshToken ?? ""), ct)
                     ?? throw DomainException.Unauthorized(InvalidSession);

        if (stored.IsRevoked)
        {
            // An already-rotated token was presented again: someone else has a copy. Kill the session.
            await RevokeFamilyAsync(stored.FamilyId, "Refresh token reuse detected", ct);
            await unitOfWork.SaveChangesAsync(ct);
            throw DomainException.Unauthorized(InvalidSession);
        }
        if (stored.IsExpired(Now))
            throw DomainException.Unauthorized(InvalidSession);

        var user = await users.FindByIdAcrossTenantsAsync(stored.UserId, ct);
        if (user is null || !user.IsActive)
        {
            stored.Revoke(Now, "User no longer active");
            await unitOfWork.SaveChangesAsync(ct);
            throw DomainException.Unauthorized(InvalidSession);
        }

        var response = Issue(user, stored.FamilyId, out var replacement);
        stored.Revoke(Now, "Rotated", replacedBy: replacement.Id);
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var stored = await refreshTokens.FindByHashAsync(tokenGenerator.Hash(refreshToken ?? ""), ct);
        if (stored is null) return; // logging out twice is not an error
        await RevokeFamilyAsync(stored.FamilyId, "Logged out", ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct)
    {
        foreach (var token in await refreshTokens.ListActiveForUserAsync(userId, ct))
            token.Revoke(Now, reason);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct)
    {
        foreach (var token in await refreshTokens.ListActiveInFamilyAsync(familyId, ct))
            token.Revoke(Now, reason);
    }

    private AuthResponse Issue(User user, Guid familyId, out RefreshToken refreshToken)
    {
        var plain = tokenGenerator.NewToken();
        refreshToken = new RefreshToken(user, tokenGenerator.Hash(plain), familyId, Now, options.Value.RefreshTokenLifetime);
        refreshTokens.Add(refreshToken);

        var access = accessTokens.CreateToken(user);
        return new AuthResponse(access.Token, access.ExpiresAt, plain, refreshToken.ExpiresAt, UserResponse.From(user));
    }
}

/// <summary>Checks, on every authenticated request, that the token's session is still allowed.</summary>
public interface ISessionValidator
{
    Task<bool> IsValidAsync(Guid userId, string? securityStamp, CancellationToken ct);
}

public class SessionValidator(IUserRepository users) : ISessionValidator
{
    public async Task<bool> IsValidAsync(Guid userId, string? securityStamp, CancellationToken ct)
    {
        var state = await users.GetSessionStateAsync(userId, ct);
        return state is { IsActive: true } && securityStamp is not null && state.SecurityStamp == securityStamp;
    }
}
