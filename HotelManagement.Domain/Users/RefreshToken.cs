namespace HotelManagement.Domain;

/// <summary>
/// A long-lived, single-use credential for getting new short-lived access tokens.
/// Only a hash is stored. Each refresh replaces the token with a new one in the same
/// <see cref="FamilyId"/> (one family per login session); presenting an already-used
/// token means it was stolen, so the whole family is revoked.
/// </summary>
public class RefreshToken : TenantEntity
{
    private RefreshToken() { }

    public RefreshToken(User user, string tokenHash, Guid familyId, DateTime now, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(user);
        AssignCompany(user.CompanyId);
        UserId = user.Id;
        TokenHash = string.IsNullOrEmpty(tokenHash) ? throw new ArgumentException("Hash required.", nameof(tokenHash)) : tokenHash;
        FamilyId = familyId;
        CreatedAt = now;
        ExpiresAt = now + lifetime;
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public Guid FamilyId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsRevoked => RevokedAt is not null;
    public bool IsExpired(DateTime now) => now >= ExpiresAt;
    public bool IsUsable(DateTime now) => !IsRevoked && !IsExpired(now);

    public void Revoke(DateTime now, string reason, Guid? replacedBy = null)
    {
        if (IsRevoked) return;
        RevokedAt = now;
        RevocationReason = reason;
        ReplacedByTokenId = replacedBy;
    }
}
