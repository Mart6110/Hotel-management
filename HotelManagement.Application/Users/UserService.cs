using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Users;

/// <summary>Write-side access to users.</summary>
public interface IUserRepository
{
    /// <summary>Within the current company.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Across all companies: used for login, before the tenant is known.</summary>
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);

    /// <summary>Across all companies: emails are globally unique logins.</summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);

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
        if (await users.EmailExistsAsync(email, ct))
            throw DomainException.Conflict("A user with this email already exists.");

        var user = new User(companyId, email, request.FullName, request.Role, passwordHasher.Hash(request.Password));
        users.Add(user);
        await unitOfWork.SaveChangesAsync(ct);
        return UserResponse.From(user);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = (await users.GetByIdAsync(id, ct)).OrThrow(id);
        user.Update(request.FullName, request.Role, request.IsActive, changedBy: currentUser.UserId);
        await unitOfWork.SaveChangesAsync(ct);
        return UserResponse.From(user);
    }
}
