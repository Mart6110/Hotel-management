using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Hotels;
using HotelManagement.Domain;

namespace HotelManagement.Application.PublicBooking;

/// <summary>What anonymous visitors can browse: a company's hotels and their availability.</summary>
public interface IPublicCatalogService
{
    Task<PagedResult<PublicHotelResponse>> ListHotelsAsync(string companySlug, PageQuery page, CancellationToken ct);
    Task<IReadOnlyList<RoomOfferResponse>> GetOffersAsync(string companySlug, Guid hotelId, OfferQuery query, CancellationToken ct);
}

public class PublicCatalogService(
    IPublicTenantResolver tenants,
    IPublicCatalogQueries catalog,
    IHotelRepository hotels,
    TimeProvider clock) : IPublicCatalogService
{
    public async Task<PagedResult<PublicHotelResponse>> ListHotelsAsync(string companySlug, PageQuery page, CancellationToken ct)
    {
        await tenants.EnterAsync(companySlug, ct);
        return await catalog.ListHotelsAsync(page, ct);
    }

    public async Task<IReadOnlyList<RoomOfferResponse>> GetOffersAsync(
        string companySlug, Guid hotelId, OfferQuery q, CancellationToken ct)
    {
        if (q.CheckIn is not { } checkIn || q.CheckOut is not { } checkOut)
            throw DomainException.Validation("CheckIn and CheckOut are required.");
        Booking.ValidateStay(checkIn, checkOut, clock.GetUtcNow().UtcDateTime);

        await tenants.EnterAsync(companySlug, ct);
        (await hotels.GetByIdAsync(hotelId, ct)).OrThrow(hotelId);

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var availability = await catalog.GetAvailabilityAsync(hotelId, checkIn, checkOut, q.Guests, ct);
        return availability
            .OrderBy(a => a.MinPricePerNight)
            .Select(a => new RoomOfferResponse(
                a.RoomType, a.MaxCapacity, a.AvailableRooms, a.MinPricePerNight, nights, a.MinPricePerNight * nights))
            .ToList();
    }
}
