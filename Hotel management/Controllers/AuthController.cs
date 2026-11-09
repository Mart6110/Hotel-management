using HotelManagement.Api.RateLimiting;
using HotelManagement.Application.Auth;
using HotelManagement.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth, IAuthSessionService sessions) : ControllerBase
{
    /// <summary>Signs up a new company and its first Admin user, and starts a session.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterCompanyRequest request, CancellationToken ct) =>
        CreatedAtAction(nameof(Me), null, await auth.RegisterCompanyAsync(request, ct));

    /// <summary>
    /// Returns a short-lived access token (15 min) and a refresh token (14 days, single use).
    /// After 5 failed attempts in a row the account is locked for 15 minutes.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, ct);

    /// <summary>Exchanges a refresh token for a new access + refresh token pair. The old refresh token stops working.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Refresh(RefreshTokenRequest request, CancellationToken ct) =>
        sessions.RefreshAsync(request.RefreshToken, ct);

    /// <summary>Ends the session: its refresh token (and any rotated from it) stops working.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await sessions.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [HttpGet("me")]
    public Task<UserResponse> Me(CancellationToken ct) => auth.GetCurrentUserAsync(ct);
}
