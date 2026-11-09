using HotelManagement.Api.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/guests")]
public class GuestsController(IGuestService guests, IGuestPrivacyService privacy) : ControllerBase
{
    /// <summary>GDPR right of access: everything held about the guest, including bookings and payments.</summary>
    [HttpGet("{id:guid}/export")]
    [Authorize(Roles = Roles.AdminOrManager)]
    public Task<GuestDataExport> Export(Guid id, CancellationToken ct) => privacy.ExportAsync(id, ct);

    /// <summary>
    /// GDPR right to erasure: irreversibly removes the guest's name, email, phone and booking notes.
    /// Bookings and payments are kept (anonymized) for accounting. Not allowed while a stay is upcoming or ongoing.
    /// </summary>
    [HttpPost("{id:guid}/anonymize")]
    [Authorize(Roles = Roles.AdminOrManager)]
    public Task<GuestResponse> Anonymize(Guid id, CancellationToken ct) => privacy.AnonymizeAsync(id, ct);

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
