using HotelManagement.Api.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Rooms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController(IRoomService rooms) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<RoomResponse>> List([FromQuery] RoomQuery query, CancellationToken ct) => rooms.ListAsync(query, ct);

    /// <summary>Active rooms in a hotel that are free for the whole stay and fit the party size.</summary>
    [HttpGet("available")]
    public Task<PagedResult<RoomResponse>> Available([FromQuery] AvailabilityQuery query, CancellationToken ct) =>
        rooms.ListAvailableAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<RoomResponse> Get(Guid id, CancellationToken ct) => rooms.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Roles = Roles.AdminOrManager)]
    public async Task<ActionResult<RoomResponse>> Create(RoomRequest request, CancellationToken ct)
    {
        var room = await rooms.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = room.Id }, room);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.AdminOrManager)]
    public Task<RoomResponse> Update(Guid id, RoomRequest request, CancellationToken ct) => rooms.UpdateAsync(id, request, ct);

    /// <summary>Deletes a room. Rooms with booking history can't be deleted; set IsActive=false instead.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.AdminOrManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await rooms.DeleteAsync(id, ct);
        return NoContent();
    }
}
