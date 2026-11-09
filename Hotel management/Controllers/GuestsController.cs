using HotelManagement.Api.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/guests")]
public class GuestsController(IGuestService guests) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<GuestResponse>> List([FromQuery] GuestQuery query, CancellationToken ct) => guests.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<GuestResponse> Get(Guid id, CancellationToken ct) => guests.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<GuestResponse>> Create(GuestRequest request, CancellationToken ct)
    {
        var guest = await guests.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = guest.Id }, guest);
    }

    [HttpPut("{id:guid}")]
    public Task<GuestResponse> Update(Guid id, GuestRequest request, CancellationToken ct) => guests.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.AdminOrManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await guests.DeleteAsync(id, ct);
        return NoContent();
    }
}
