using HotelManagement.Application.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.PublicBooking;

public class PublicBookingOptions
{
    /// <summary>Guests can cancel online until this many hours before the start of the arrival day.</summary>
    public int CancellationCutoffHours { get; set; } = 24;

    public TimeSpan CancellationCutoff => TimeSpan.FromHours(CancellationCutoffHours);
}

/// <summary>Availability for one room type, as computed by the read side.</summary>
public record RoomTypeAvailability(RoomType RoomType, int MaxCapacity, int AvailableRooms, decimal MinPricePerNight);

/// <summary>Read-side queries for the public catalog, within the selected company.</summary>
public interface IPublicCatalogQueries
{
    Task<PagedResult<PublicHotelResponse>> ListHotelsAsync(PageQuery page, CancellationToken ct);

    Task<IReadOnlyList<RoomTypeAvailability>> GetAvailabilityAsync(
        Guid hotelId, DateOnly checkIn, DateOnly checkOut, int guests, CancellationToken ct);
}

/// <summary>Resolves the company from a public URL slug and scopes the request to it.</summary>
public interface IPublicTenantResolver
{
    Task<Company> EnterAsync(string companySlug, CancellationToken ct);
}

public class PublicTenantResolver(ICompanyRepository companies, ITenantSelector tenant) : IPublicTenantResolver
{
    public async Task<Company> EnterAsync(string companySlug, CancellationToken ct)
    {
        var slug = (companySlug ?? "").Trim().ToLowerInvariant();
        var company = await companies.FindBySlugAsync(slug, ct)
                      ?? throw DomainException.NotFound(nameof(Company), slug);
        tenant.Select(company.Id);
        return company;
    }
}
