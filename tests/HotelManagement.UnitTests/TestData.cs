using HotelManagement.Domain;

namespace HotelManagement.UnitTests;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = DateOnly.FromDateTime(Now);
    public static readonly Guid CompanyId = Guid.NewGuid();

    public static Hotel NewHotel()
    {
        var hotel = new Hotel("Test Hotel", "1 Street", "City", "Country", null, null);
        hotel.AssignCompany(CompanyId);
        return hotel;
    }

    public static Room NewRoom(decimal price = 100m, int capacity = 2, bool isActive = true) =>
        new(NewHotel(), "101", RoomType.Double, capacity, price, 1, isActive);

    public static Guest NewGuest()
    {
        var guest = new Guest("Jane", "Doe", "JANE@Example.com ", null);
        guest.AssignCompany(CompanyId);
        return guest;
    }

    /// <summary>A confirmed booking from today+<paramref name="startOffset"/> for <paramref name="nights"/> nights.</summary>
    public static Booking NewBooking(decimal price = 100m, int startOffset = 0, int nights = 2) =>
        global::HotelManagement.Domain.Booking.Create(NewRoom(price), NewGuest(), Today.AddDays(startOffset), Today.AddDays(startOffset + nights), 1, null, Now, "BK-TEST0001");
}
