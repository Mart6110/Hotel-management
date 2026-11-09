namespace HotelManagement.Domain;

/// <summary>A tenant: a hotel company using the system.</summary>
public class Company : Entity
{
    private Company() { }

    public Company(string name)
    {
        Name = Guard.Required(name, "Company name", 200);
    }

    public string Name { get; private set; } = "";
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;
}
