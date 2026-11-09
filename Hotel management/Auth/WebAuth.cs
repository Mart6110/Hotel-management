using System.Security.Claims;
using HotelManagement.Application.Common;
using HotelManagement.Domain;
using HotelManagement.Infrastructure.Security;

namespace HotelManagement.Api.Auth;

/// <summary>Role names for [Authorize(Roles = ...)], derived from the domain enum.</summary>
public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Manager = nameof(UserRole.Manager);
    public const string Receptionist = nameof(UserRole.Receptionist);
    public const string AdminOrManager = Admin + "," + Manager;
}

/// <summary>Reads the caller's identity from the validated JWT on the current request.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Parse(AppClaims.Subject);
    public Guid? CompanyId => Parse(AppClaims.CompanyId);

    private Guid? Parse(string claim) =>
        Guid.TryParse(Principal?.FindFirst(claim)?.Value, out var id) ? id : null;
}
