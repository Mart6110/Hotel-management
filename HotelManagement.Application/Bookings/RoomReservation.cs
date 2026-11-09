using HotelManagement.Application.Common;
using HotelManagement.Application.Rooms;
using HotelManagement.Domain;

namespace HotelManagement.Application.Bookings;

public record ReservationRequest(DateOnly CheckIn, DateOnly CheckOut, int NumberOfGuests, string? Notes);

/// <summary>
/// The single place that reserves a room, used by both staff and online booking.
/// Must be called inside a transaction: it row-locks the room so concurrent
/// reservations for the same room are serialized and the overlap check can't race.
/// </summary>
public interface IRoomReservation
{
    /// <returns>The new (added, unsaved) booking, or <c>null</c> if the room is taken for those dates.</returns>
    Task<Booking?> TryReserveAsync(
        Guid roomId, Guest guest, ReservationRequest request,
        BookingSource source, string? accessCodeHash, CancellationToken ct);
}

public class RoomReservation(
    IRoomRepository rooms,
    IBookingRepository bookings,
    IBookingCodeGenerator codes,
    TimeProvider clock) : IRoomReservation
{
    public async Task<Booking?> TryReserveAsync(
        Guid roomId, Guest guest, ReservationRequest r, BookingSource source, string? accessCodeHash, CancellationToken ct)
    {
        await rooms.LockForBookingAsync(roomId, ct);
        var room = (await rooms.GetByIdAsync(roomId, ct)).OrThrow(roomId);

        // Domain validation (dates, capacity, active room) runs before the availability check.
        var booking = Booking.Create(
            room, guest, r.CheckIn, r.CheckOut, r.NumberOfGuests, r.Notes,
            clock.GetUtcNow().UtcDateTime, codes.NewReference(), source, accessCodeHash);

        if (await bookings.HasOverlapAsync(room.Id, booking.CheckIn, booking.CheckOut, ct))
            return null;

        bookings.Add(booking);
        return booking;
    }
}
