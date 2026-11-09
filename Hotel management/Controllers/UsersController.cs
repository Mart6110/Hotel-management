using HotelManagement.Api.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Controllers;

/// <summary>Staff accounts within the caller's company. Admin only.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserResponse>> List([FromQuery] UserQuery query, CancellationToken ct) => users.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<UserResponse> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    public Task<UserResponse> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => users.UpdateAsync(id, request, ct);
}
