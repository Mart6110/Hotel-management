using HotelManagement.Api.RateLimiting;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.PublicBooking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HotelManagement.Api.Controllers;

/// <summary>Guest-facing catalog: no account needed. The company is identified by its slug.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/public/{companySlug}")]
[Tags("Public: catalog")]
public class PublicCatalogController(IPublicCatalogService catalog) : ControllerBase
{
    [HttpGet("hotels")]
    public Task<PagedResult<PublicHotelResponse>> Hotels(string companySlug, [FromQuery] PageQuery page, CancellationToken ct) =>
        catalog.ListHotelsAsync(companySlug, page, ct);

    /// <summary>Available room types for a stay, with "from" prices. Book one with POST /bookings.</summary>
    [HttpGet("hotels/{hotelId:guid}/offers")]
    public Task<IReadOnlyList<RoomOfferResponse>> Offers(string companySlug, Guid hotelId, [FromQuery] OfferQuery query, CancellationToken ct) =>
        catalog.GetOffersAsync(companySlug, hotelId, query, ct);
}

/// <summary>Guest self-service bookings. Viewing and cancelling need the reference plus the secret access code.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/public/{companySlug}/bookings")]
[Tags("Public: bookings")]
public class PublicBookingsController(IGuestBookingService bookings) : ControllerBase
{
    public const string AccessCodeHeader = "X-Booking-Code";

    /// <summary>Books the cheapest free room of the requested type. The response contains the access code: it is shown only once.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    [ProducesResponseType<PublicBookingCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PublicBookingCreatedResponse>> Create(string companySlug, PublicBookingRequest request, CancellationToken ct)
    {
        var created = await bookings.CreateAsync(companySlug, request, ct);
        return CreatedAtAction(nameof(Get), new { companySlug, reference = created.Booking.Reference }, created);
    }

    [HttpGet("{reference}")]
    public Task<PublicBookingResponse> Get(
        string companySlug, string reference, [FromHeader(Name = AccessCodeHeader)] string accessCode, CancellationToken ct) =>
        bookings.GetAsync(companySlug, reference, accessCode, ct);

    /// <summary>Cancels the booking, allowed until the cancellation deadline (by default 24 hours before arrival day).</summary>
    [HttpPost("{reference}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    public Task<PublicBookingResponse> Cancel(
        string companySlug, string reference, [FromHeader(Name = AccessCodeHeader)] string accessCode, CancellationToken ct) =>
        bookings.CancelAsync(companySlug, reference, accessCode, ct);
}
