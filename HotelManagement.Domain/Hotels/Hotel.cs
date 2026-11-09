namespace HotelManagement.Domain;

public class Hotel : TenantEntity
{
    private Hotel() { }

    public Hotel(string name, string address, string city, string country, string? phone, string? email)
    {
        Update(name, address, city, country, phone, email);
    }

    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string City { get; private set; } = "";
    public string Country { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;

    public string FullAddress => $"{Address}, {City}, {Country}";

    public void Update(string name, string address, string city, string country, string? phone, string? email)
    {
        Name = Guard.Required(name, "Name", 200);
        Address = Guard.Required(address, "Address", 300);
        City = Guard.Required(city, "City", 100);
        Country = Guard.Required(country, "Country", 100);
        Phone = Guard.Optional(phone, "Phone", 50);
        Email = Guard.OptionalEmail(email, "Email");
    }
}
