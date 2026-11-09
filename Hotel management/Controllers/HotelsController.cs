using HotelManagement.Api.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Hotels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/hotels")]
public class HotelsController(IHotelService hotels) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<HotelResponse>> List([FromQuery] HotelQuery query, CancellationToken ct) => hotels.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<HotelResponse> Get(Guid id, CancellationToken ct) => hotels.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Roles = Roles.AdminOrManager)]
    public async Task<ActionResult<HotelResponse>> Create(HotelRequest request, CancellationToken ct)
    {
        var hotel = await hotels.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = hotel.Id }, hotel);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.AdminOrManager)]
    public Task<HotelResponse> Update(Guid id, HotelRequest request, CancellationToken ct) => hotels.UpdateAsync(id, request, ct);

    /// <summary>Deletes a hotel. Fails with 409 while it still has rooms.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await hotels.DeleteAsync(id, ct);
        return NoContent();
    }
}
