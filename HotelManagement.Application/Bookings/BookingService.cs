using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Rooms;
using HotelManagement.Domain;

namespace HotelManagement.Application.Bookings;

/// <summary>Reservation lifecycle: create, check in, check out, cancel.</summary>
public interface IBookingService
{
    Task<PagedResult<BookingResponse>> ListAsync(BookingQuery query, CancellationToken ct);
    Task<BookingResponse> GetAsync(Guid id, CancellationToken ct);
    Task<BookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken ct);
    Task<BookingResponse> CheckInAsync(Guid id, CancellationToken ct);
    Task<BookingResponse> CheckOutAsync(Guid id, CancellationToken ct);
    Task<BookingResponse> CancelAsync(Guid id, CancellationToken ct);
}

public class BookingService(
    IBookingRepository bookings,
    IRoomRepository rooms,
    IGuestRepository guests,
    IBookingQueries queries,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IBookingService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<PagedResult<BookingResponse>> ListAsync(BookingQuery query, CancellationToken ct) => queries.ListAsync(query, ct);

    public async Task<BookingResponse> GetAsync(Guid id, CancellationToken ct) =>
        (await queries.GetAsync(id, ct)) ?? throw DomainException.NotFound(nameof(Booking), id);

    public async Task<BookingResponse> CreateAsync(CreateBookingRequest r, CancellationToken ct)
    {
        Guid bookingId;
        await using (var tx = await unitOfWork.BeginTransactionAsync(ct))
        {
            // Serialize bookings per room so the overlap check below can't race.
            // (The database's exclusion constraint remains the final guarantee.)
            await rooms.LockForBookingAsync(r.RoomId, ct);

            var room = (await rooms.GetByIdAsync(r.RoomId, ct)).OrThrow(r.RoomId);
            var guest = (await guests.GetByIdAsync(r.GuestId, ct)).OrThrow(r.GuestId);
            var booking = Booking.Create(room, guest, r.CheckIn, r.CheckOut, r.NumberOfGuests, r.Notes, Now);

            if (await bookings.HasOverlapAsync(room.Id, booking.CheckIn, booking.CheckOut, ct))
                throw DomainException.Conflict("The room is already booked for the selected dates.");

            bookings.Add(booking);
            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            bookingId = booking.Id;
        }
        return await GetAsync(bookingId, ct);
    }

    public Task<BookingResponse> CheckInAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, b => b.MarkCheckedIn(Now), ct);

    public Task<BookingResponse> CheckOutAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, b => b.MarkCheckedOut(Now), ct);

    public Task<BookingResponse> CancelAsync(Guid id, CancellationToken ct) =>
        ChangeAsync(id, b => b.Cancel(Now), ct);

    private async Task<BookingResponse> ChangeAsync(Guid id, Action<Booking> change, CancellationToken ct)
    {
        var booking = (await bookings.GetByIdAsync(id, ct)).OrThrow(id);
        change(booking);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }
}
