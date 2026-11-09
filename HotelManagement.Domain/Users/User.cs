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

    public static string NormalizeEmail(string email) => Guard.Required(email, "Email", 256).ToLowerInvariant();

    /// <param name="changedBy">The user making the change; admins can't lock themselves out.</param>
    public void Update(string fullName, UserRole role, bool isActive, Guid? changedBy)
    {
        if (changedBy == Id && (role != UserRole.Admin || !isActive) && Role == UserRole.Admin)
            throw DomainException.Conflict("You cannot demote or deactivate your own account.");

        FullName = Guard.Required(fullName, "Full name", 200);
        Role = role;
        IsActive = isActive;
    }
}
