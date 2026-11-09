using HotelManagement.Application.Auth;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Guests;

/// <summary>GDPR data-subject requests for guests: export everything held about them, or erase it.</summary>
public interface IGuestPrivacyService
{
    Task<GuestDataExport> ExportAsync(Guid guestId, CancellationToken ct);
    Task<GuestResponse> AnonymizeAsync(Guid guestId, CancellationToken ct);
}

public class GuestPrivacyService(
    IGuestRepository guests,
    IBookingRepository bookings,
    ICompanyRepository companies,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IGuestPrivacyService
{
    public async Task<GuestDataExport> ExportAsync(Guid guestId, CancellationToken ct)
    {
        var guest = (await guests.GetByIdAsync(guestId, ct)).OrThrow(guestId);
        var company = (await companies.GetByIdAsync(guest.CompanyId, ct)).OrThrow(guest.CompanyId);
        var stays = await bookings.ListForGuestAsync(guestId, ct);

        return new GuestDataExport(
            clock.GetUtcNow().UtcDateTime,
            company.Name,
            new GuestPersonalData(guest.Id, guest.FirstName, guest.LastName, guest.Email, guest.Phone, guest.CreatedAt, guest.AnonymizedAt),
            stays.OrderBy(b => b.CheckIn).Select(b => new GuestBookingData(
                b.Reference, b.Source, b.Status, b.Room.Hotel.Name, b.Room.Type, b.CheckIn, b.CheckOut,
                b.NumberOfGuests, b.TotalPrice, b.Notes, b.CreatedAt,
                b.Payments.OrderBy(p => p.PaidAt).Select(PaymentResponse.From).ToList())).ToList());
    }

    public async Task<GuestResponse> AnonymizeAsync(Guid guestId, CancellationToken ct)
    {
        var guest = (await guests.GetByIdAsync(guestId, ct)).OrThrow(guestId);
        var stays = await bookings.ListForGuestAsync(guestId, ct);
        if (stays.Any(b => b.IsActive))
            throw DomainException.Conflict(
                "The guest has upcoming or current stays. Cancel or complete them before anonymizing.");

        // Bookings and payments are kept for accounting, but no longer identify the person.
        guest.Anonymize(clock.GetUtcNow().UtcDateTime);
        foreach (var booking in stays)
            booking.RedactNotes();

        await unitOfWork.SaveChangesAsync(ct);
        return GuestResponse.From(guest);
    }
}
