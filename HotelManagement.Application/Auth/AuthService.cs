using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Users;
using HotelManagement.Domain;

namespace HotelManagement.Application.Auth;

public interface ICompanyRepository
{
    void Add(Company company);
}

public interface IAuthService
{
    /// <summary>Signs up a new company with its first Admin user.</summary>
    Task<AuthResponse> RegisterCompanyAsync(RegisterCompanyRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UserResponse> GetCurrentUserAsync(CancellationToken ct);
}

public class AuthService(
    ICompanyRepository companies,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ICurrentUser currentUser) : IAuthService
{
    public async Task<AuthResponse> RegisterCompanyAsync(RegisterCompanyRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        if (await users.EmailExistsAsync(email, ct))
            throw DomainException.Conflict("A user with this email already exists.");

        var company = new Company(request.CompanyName);
        var admin = new User(company.Id, email, request.FullName, UserRole.Admin, passwordHasher.Hash(request.Password));
        companies.Add(company);
        users.Add(admin);
        await unitOfWork.SaveChangesAsync(ct);

        return CreateResponse(admin);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), ct);
        if (user is null || !user.IsActive || !passwordHasher.Verify(user.PasswordHash, request.Password))
            throw DomainException.Unauthorized("Invalid email or password.");

        return CreateResponse(user);
    }

    public async Task<UserResponse> GetCurrentUserAsync(CancellationToken ct)
    {
        var id = currentUser.UserId ?? throw DomainException.Unauthorized("Not signed in.");
        var user = (await users.GetByIdAsync(id, ct)).OrThrow(id);
        return UserResponse.From(user);
    }

    private AuthResponse CreateResponse(User user)
    {
        var token = tokenService.CreateToken(user);
        return new AuthResponse(token.Token, token.ExpiresAt, UserResponse.From(user));
    }
}
