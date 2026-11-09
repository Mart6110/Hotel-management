using System.ComponentModel.DataAnnotations;
using HotelManagement.Domain;

namespace HotelManagement.Application.Contracts;

// Contracts for the anonymous, guest-facing booking API. They deliberately expose
// less than the staff contracts: no internal IDs of guests, no room numbers.

public record PublicHotelResponse(Guid Id, string Name, string Address, string City, string Country, string? Phone, string? Email);

public class OfferQuery
{
    [Required] public DateOnly? CheckIn { get; set; }
    [Required] public DateOnly? CheckOut { get; set; }
    [Range(1, Room.MaxCapacity)] public int Guests { get; set; } = 1;
}

/// <summary>Availability for one room type. Booking it reserves the cheapest free room of that type.</summary>
public record RoomOfferResponse(
    RoomType RoomType,
    int MaxCapacity,
    int AvailableRooms,
    decimal PricePerNightFrom,
    int Nights,
    decimal TotalPriceFrom);

public record PublicBookingRequest
{
    [Required] public Guid HotelId { get; init; }
    [Required, EnumDataType(typeof(RoomType))] public RoomType RoomType { get; init; }
    [Required] public DateOnly CheckIn { get; init; }
    [Required] public DateOnly CheckOut { get; init; }
    [Range(1, Room.MaxCapacity)] public int NumberOfGuests { get; init; } = 1;
    [Required, MaxLength(100)] public string FirstName { get; init; } = "";
    [Required, MaxLength(100)] public string LastName { get; init; } = "";
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = "";
    [MaxLength(50)] public string? Phone { get; init; }
    [MaxLength(1000)] public string? Notes { get; init; }
}

public record PublicBookingResponse(
    string Reference,
    BookingStatus Status,
    string HotelName,
    string HotelAddress,
    RoomType RoomType,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int NumberOfGuests,
    decimal PricePerNight,
    decimal TotalPrice,
    string GuestName,
    string? GuestEmail,
    DateTime? CancellableUntil, // UTC; null once online cancellation no longer applies
    DateTime CreatedAt);

/// <summary>Returned once, at booking time: the access code is never shown again (it is stored hashed).</summary>
public record PublicBookingCreatedResponse(string AccessCode, PublicBookingResponse Booking);
