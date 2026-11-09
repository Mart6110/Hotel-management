using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using HotelManagement.Domain;

namespace HotelManagement.Application.Bookings;

/// <summary>Reservation lifecycle for staff: create, check in, check out, cancel.</summary>
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
    IGuestRepository guests,
    IRoomReservation reservation,
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
            var guest = (await guests.GetByIdAsync(r.GuestId, ct)).OrThrow(r.GuestId);
            var booking = await reservation.TryReserveAsync(
                    r.RoomId, guest, new ReservationRequest(r.CheckIn, r.CheckOut, r.NumberOfGuests, r.Notes),
                    BookingSource.Staff, accessCodeHash: null, ct)
                ?? throw DomainException.Conflict("The room is already booked for the selected dates.");

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
