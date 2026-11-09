namespace HotelManagement.Domain;

public class Guest : TenantEntity
{
    public const string AnonymizedFirstName = "Anonymized";
    public const string AnonymizedLastName = "Guest";

    private Guest() { }

    public Guest(string firstName, string lastName, string? email, string? phone)
    {
        Update(firstName, lastName, email, phone);
    }

    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;

    /// <summary>Set once personal data has been erased (GDPR "right to erasure").</summary>
    public DateTime? AnonymizedAt { get; private set; }

    public bool IsAnonymized => AnonymizedAt is not null;
    public string FullName => $"{FirstName} {LastName}";

    public void Update(string firstName, string lastName, string? email, string? phone)
    {
        if (IsAnonymized)
            throw DomainException.Conflict("An anonymized guest can't be edited.");

        FirstName = Guard.Required(firstName, "First name", 100);
        LastName = Guard.Required(lastName, "Last name", 100);
        Email = Guard.OptionalEmail(email, "Email");
        Phone = Guard.Optional(phone, "Phone", 50);
    }

    /// <summary>
    /// Irreversibly removes the guest's personal data while keeping the record, so
    /// bookings and payments stay intact for accounting.
    /// </summary>
    public void Anonymize(DateTime now)
    {
        if (IsAnonymized) return;
        FirstName = AnonymizedFirstName;
        LastName = AnonymizedLastName;
        Email = null;
        Phone = null;
        AnonymizedAt = now;
    }
}
