using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.Rooms;
using HotelManagement.Domain;
using Microsoft.Extensions.Options;

namespace HotelManagement.Application.PublicBooking;

/// <summary>Self-service bookings for guests without an account: book, look up, cancel.</summary>
public interface IGuestBookingService
{
    Task<PublicBookingCreatedResponse> CreateAsync(string companySlug, PublicBookingRequest request, CancellationToken ct);
    Task<PublicBookingResponse> GetAsync(string companySlug, string reference, string accessCode, CancellationToken ct);
    Task<PublicBookingResponse> CancelAsync(string companySlug, string reference, string accessCode, CancellationToken ct);
}

public class GuestBookingService(
    IPublicTenantResolver tenants,
    IHotelRepository hotels,
    IRoomRepository rooms,
    IGuestRepository guests,
    IBookingRepository bookings,
    IRoomReservation reservation,
    IBookingCodeGenerator codes,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork,
    IBookingEmailNotifier notifier,
    IOptions<PublicBookingOptions> options,
    TimeProvider clock) : IGuestBookingService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private TimeSpan Cutoff => options.Value.CancellationCutoff;

    public async Task<PublicBookingCreatedResponse> CreateAsync(string companySlug, PublicBookingRequest r, CancellationToken ct)
    {
        Booking.ValidateStay(r.CheckIn, r.CheckOut, Now);
        var company = await tenants.EnterAsync(companySlug, ct);
        var hotel = (await hotels.GetByIdAsync(r.HotelId, ct)).OrThrow(r.HotelId);

        // The plain code goes to the guest once; only its hash is stored.
        var accessCode = codes.NewAccessCode();
        var accessCodeHash = hasher.Hash(accessCode);

        Booking? booking = null;
        await using (var tx = await unitOfWork.BeginTransactionAsync(ct))
        {
            var guest = await FindOrCreateGuestAsync(company, r, ct);
            var request = new ReservationRequest(r.CheckIn, r.CheckOut, r.NumberOfGuests, r.Notes);

            // Try the cheapest free room first; if another request grabbed it meanwhile, move on to the next.
            var candidates = await rooms.FindFreeRoomIdsAsync(hotel.Id, r.RoomType, r.CheckIn, r.CheckOut, r.NumberOfGuests, ct);
            foreach (var roomId in candidates)
            {
                booking = await reservation.TryReserveAsync(roomId, guest, request, BookingSource.Online, accessCodeHash, ct);
                if (booking is not null) break;
            }
            if (booking is null)
                throw DomainException.Conflict($"No {r.RoomType} rooms are available at {hotel.Name} for those dates.");

            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var response = ToResponse(booking, hotel);
        await notifier.BookingConfirmedAsync(company, response, accessCode, ct);
        return new PublicBookingCreatedResponse(accessCode, response);
    }

    public async Task<PublicBookingResponse> GetAsync(string companySlug, string reference, string accessCode, CancellationToken ct)
    {
        await tenants.EnterAsync(companySlug, ct);
        var booking = await FindAuthorizedAsync(reference, accessCode, ct);
        return ToResponse(booking, booking.Room.Hotel);
    }

    public async Task<PublicBookingResponse> CancelAsync(string companySlug, string reference, string accessCode, CancellationToken ct)
    {
        var company = await tenants.EnterAsync(companySlug, ct);
        var booking = await FindAuthorizedAsync(reference, accessCode, ct);

        booking.CancelByGuest(Now, Cutoff);
        await unitOfWork.SaveChangesAsync(ct);

        var response = ToResponse(booking, booking.Room.Hotel);
        await notifier.BookingCancelledAsync(company, response, ct);
        return response;
    }

    /// <summary>Unknown reference and wrong code look identical (404), so references can't be probed.</summary>
    private async Task<Booking> FindAuthorizedAsync(string reference, string accessCode, CancellationToken ct)
    {
        var normalized = (reference ?? "").Trim().ToUpperInvariant();
        var booking = await bookings.FindByReferenceAsync(normalized, ct);

        // Always hash-check (even when the booking doesn't exist or has no code) so timing reveals nothing.
        var check = hasher.Verify(booking?.AccessCodeHash, (accessCode ?? "").Trim().ToUpperInvariant());
        if (booking is null || check == PasswordCheck.Failed)
            throw DomainException.NotFound(nameof(Booking), normalized);
        return booking;
    }

    /// <summary>Returning guests are matched by email, so staff see one guest record per person.</summary>
    private async Task<Guest> FindOrCreateGuestAsync(Company company, PublicBookingRequest r, CancellationToken ct)
    {
        var email = User.NormalizeEmail(r.Email);
        var existing = await guests.FindByEmailAsync(email, ct);
        if (existing is not null)
            return existing;

        var guest = new Guest(r.FirstName, r.LastName, email, r.Phone);
        guest.AssignCompany(company.Id);
        guests.Add(guest);
        return guest;
    }

    private PublicBookingResponse ToResponse(Booking b, Hotel hotel)
    {
        DateTime? cancellableUntil = null;
        if (b.Status == BookingStatus.Confirmed && b.Source == BookingSource.Online)
        {
            var deadline = b.GuestCancellationDeadline(Cutoff);
            if (Now <= deadline) cancellableUntil = deadline;
        }

        return new PublicBookingResponse(
            b.Reference, b.Status, hotel.Name, hotel.FullAddress, b.Room.Type,
            b.CheckIn, b.CheckOut, b.Nights, b.NumberOfGuests, b.PricePerNight, b.TotalPrice,
            b.Guest.FullName, b.Guest.Email, cancellableUntil, b.CreatedAt);
    }
}
