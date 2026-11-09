namespace HotelManagement.Domain;

public enum UserRole
{
    Admin,
    Manager,
    Receptionist
}

/// <summary>A staff account belonging to one company.</summary>
public class User : TenantEntity
{
    private User() { }

    public User(Guid companyId, string email, string fullName, UserRole role, string passwordHash)
    {
        AssignCompany(companyId);
        Email = NormalizeEmail(email);
        FullName = Guard.Required(fullName, "Full name", 200);
        Role = role;
        PasswordHash = passwordHash;
    }

    public string Email { get; private set; } = "";
    public string FullName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;

    /// <summary>
    /// Changes whenever the user's access must be re-evaluated (deactivated, role changed).
    /// Access tokens carry the stamp; a mismatch makes them invalid immediately.
    /// </summary>
    public string SecurityStamp { get; private set; } = NewSecurityStamp();

    /// <summary>Consecutive failed logins since the last success or lockout.</summary>
    public int FailedLoginCount { get; private set; }

    /// <summary>While in the future, logins are refused even with the right password.</summary>
    public DateTime? LockoutEndsAt { get; private set; }

    public static string NormalizeEmail(string email) => Guard.Required(email, "Email", 256).ToLowerInvariant();

    public bool IsLockedOut(DateTime now) => LockoutEndsAt > now;

    /// <summary>Counts a failed login; after <paramref name="maxAttempts"/> in a row the account is locked for <paramref name="lockoutDuration"/>.</summary>
    public void RegisterFailedLogin(DateTime now, int maxAttempts, TimeSpan lockoutDuration)
    {
        if (IsLockedOut(now))
            return; // attempts during a lockout don't extend it
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockoutEndsAt = now + lockoutDuration;
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutEndsAt = null;
    }

    /// <summary>Replaces the stored hash, e.g. when the hashing algorithm's parameters were strengthened.</summary>
    public void ChangePasswordHash(string passwordHash) =>
        PasswordHash = string.IsNullOrEmpty(passwordHash)
            ? throw new ArgumentException("Password hash cannot be empty.", nameof(passwordHash))
            : passwordHash;

    /// <param name="changedBy">The user making the change; admins can't lock themselves out.</param>
    public void Update(string fullName, UserRole role, bool isActive, Guid? changedBy)
    {
        if (changedBy == Id && (role != UserRole.Admin || !isActive) && Role == UserRole.Admin)
            throw DomainException.Conflict("You cannot demote or deactivate your own account.");

        var accessChanged = role != Role || isActive != IsActive;
        FullName = Guard.Required(fullName, "Full name", 200);
        Role = role;
        IsActive = isActive;
        if (accessChanged)
            RotateSecurityStamp(); // existing sessions must not keep the old role/active state
    }

    /// <summary>Invalidates every access token issued so far.</summary>
    public void RotateSecurityStamp() => SecurityStamp = NewSecurityStamp();

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}
