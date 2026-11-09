namespace HotelManagement.Domain;

public class Guest : TenantEntity
{
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

    public string FullName => $"{FirstName} {LastName}";

    public void Update(string firstName, string lastName, string? email, string? phone)
    {
        FirstName = Guard.Required(firstName, "First name", 100);
        LastName = Guard.Required(lastName, "Last name", 100);
        Email = Guard.OptionalEmail(email, "Email");
        Phone = Guard.Optional(phone, "Phone", 50);
    }
}
