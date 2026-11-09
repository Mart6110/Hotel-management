using HotelManagement.Application.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Users;

/// <summary>What per-request session validation needs to know about a user.</summary>
public record UserSessionState(bool IsActive, string SecurityStamp);

/// <summary>Write-side access to users.</summary>
public interface IUserRepository
{
    /// <summary>Within the current company.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Across all companies: used by token refresh, before the tenant is known.</summary>
    Task<User?> FindByIdAcrossTenantsAsync(Guid id, CancellationToken ct);

    /// <summary>Across all companies: used for login, before the tenant is known.</summary>
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);

    /// <summary>Across all companies: emails are globally unique logins.</summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);

    /// <summary>Lightweight lookup run on every authenticated request.</summary>
    Task<UserSessionState?> GetSessionStateAsync(Guid id, CancellationToken ct);

    void Add(User user);
}

/// <summary>Read-side projections of users in the current company.</summary>
public interface IUserQueries
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken ct);
}

public interface IUserService
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken ct);
    Task<UserResponse> GetAsync(Guid id, CancellationToken ct);
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
}

public class UserService(
    IUserRepository users,
    IUserQueries queries,
    IPasswordHasher passwordHasher,
    IPasswordPolicy passwordPolicy,
    IAuthSessionService sessions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IUserService
{
    public Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken ct) => queries.ListAsync(query, ct);

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken ct) =>
        UserResponse.From((await users.GetByIdAsync(id, ct)).OrThrow(id));

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var companyId = currentUser.CompanyId ?? throw DomainException.Unauthorized("Not signed in.");
        var email = User.NormalizeEmail(request.Email);
        passwordPolicy.EnsureAcceptable(request.Password, email, request.FullName);
        if (await users.EmailExistsAsync(email, ct))
            throw DomainException.Conflict("This email address can't be used for a new account.");

        var user = new User(companyId, email, request.FullName, request.Role, passwordHasher.Hash(request.Password));
        users.Add(user);
        await unitOfWork.SaveChangesAsync(ct);
        return UserResponse.From(user);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = (await users.GetByIdAsync(id, ct)).OrThrow(id);
        var stampBefore = user.SecurityStamp;
        user.Update(request.FullName, request.Role, request.IsActive, changedBy: currentUser.UserId);

        // Role or active state changed: the new stamp already invalidates access tokens;
        // also revoke refresh tokens so no new ones can be minted with stale rights.
        if (user.SecurityStamp != stampBefore)
            await sessions.RevokeAllAsync(user.Id, "Access changed by an administrator", ct);

        await unitOfWork.SaveChangesAsync(ct);
        return UserResponse.From(user);
    }
}
