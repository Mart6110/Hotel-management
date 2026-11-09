namespace HotelManagement.Domain;

public enum RoomType
{
    Single,
    Double,
    Twin,
    Suite,
    Family
}

public class Room : TenantEntity
{
    public const int MaxCapacity = 20;

    private Room() { }

    public Room(Hotel hotel, string number, RoomType type, int capacity, decimal pricePerNight, int floor, bool isActive = true)
    {
        Update(hotel, number, type, capacity, pricePerNight, floor, isActive);
    }

    public Guid HotelId { get; private set; }
    public Hotel Hotel { get; private set; } = null!;
    public string Number { get; private set; } = "";
    public RoomType Type { get; private set; }
    public int Capacity { get; private set; }
    public decimal PricePerNight { get; private set; }
    public int Floor { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(Hotel hotel, string number, RoomType type, int capacity, decimal pricePerNight, int floor, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(hotel);
        if (CompanyId != Guid.Empty && hotel.CompanyId != Guid.Empty && hotel.CompanyId != CompanyId)
            throw DomainException.Validation("A room can only belong to a hotel of the same company.");
        InheritCompany(hotel);

        Hotel = hotel;
        HotelId = hotel.Id;
        Number = Guard.Required(number, "Room number", 20);
        Type = type;
        Capacity = Guard.InRange(capacity, "Capacity", 1, MaxCapacity);
        PricePerNight = Guard.Positive(pricePerNight, "Price per night");
        Floor = Guard.InRange(floor, "Floor", -5, 200);
        IsActive = isActive;
    }

    /// <summary>Throws unless this room can currently take a booking for the given party size.</summary>
    public void EnsureCanHost(int numberOfGuests)
    {
        if (!IsActive)
            throw DomainException.Conflict("The room is not available for booking.");
        if (numberOfGuests < 1)
            throw DomainException.Validation("A booking needs at least one guest.");
        if (numberOfGuests > Capacity)
            throw DomainException.Validation($"The room holds at most {Capacity} guests.");
    }
}
