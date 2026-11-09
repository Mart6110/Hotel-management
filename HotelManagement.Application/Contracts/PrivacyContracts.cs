using HotelManagement.Domain;

namespace HotelManagement.Application.Contracts;

// GDPR data-subject requests: right of access (export) and right to erasure (anonymize).

public record GuestDataExport(
    DateTime ExportedAt,
    string Company,
    GuestPersonalData Guest,
    IReadOnlyList<GuestBookingData> Bookings);

public record GuestPersonalData(Guid Id, string FirstName, string LastName, string? Email, string? Phone, DateTime CreatedAt, DateTime? AnonymizedAt);

public record GuestBookingData(
    string Reference,
    BookingSource Source,
    BookingStatus Status,
    string Hotel,
    RoomType RoomType,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int NumberOfGuests,
    decimal TotalPrice,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<PaymentResponse> Payments);
