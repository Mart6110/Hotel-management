using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Bookings;

public interface IBookingRepository
{
    /// <summary>Loads the booking aggregate including its payments.</summary>
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Loads the booking with room, hotel, guest and payments, read-only.</summary>
    Task<Booking?> GetWithDetailsAsync(Guid id, CancellationToken ct);

    /// <summary>Whether an active booking for the room overlaps [checkIn, checkOut).</summary>
    Task<bool> HasOverlapAsync(Guid roomId, DateOnly checkIn, DateOnly checkOut, CancellationToken ct);

    /// <summary>Takes a row lock on the booking for the current transaction.</summary>
    Task LockAsync(Guid bookingId, CancellationToken ct);

    void Add(Booking booking);
}

public interface IBookingQueries
{
    Task<PagedResult<BookingResponse>> ListAsync(BookingQuery query, CancellationToken ct);
    Task<BookingResponse?> GetAsync(Guid id, CancellationToken ct);
}
