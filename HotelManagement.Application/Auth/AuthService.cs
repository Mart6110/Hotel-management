using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Users;
using HotelManagement.Domain;
using Microsoft.Extensions.Options;

namespace HotelManagement.Application.Auth;

/// <summary>Companies are the tenants; their lookups ignore tenant filters.</summary>
public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Company?> FindBySlugAsync(string slug, CancellationToken ct);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
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
    IPasswordPolicy passwordPolicy,
    IAuthSessionService sessions,
    ICurrentUser currentUser,
    IOptions<LoginSecurityOptions> loginOptions,
    TimeProvider clock) : IAuthService
{
    /// <summary>
    /// One message for every login failure (unknown email, wrong password, locked, deactivated),
    /// so responses don't reveal which accounts exist.
    /// </summary>
    public const string LoginFailedMessage =
        "Invalid email or password, or the account is temporarily locked after too many failed attempts.";

    /// <summary>Deliberately vague so registration can't be used to confirm who has an account.</summary>
    public const string EmailUnavailableMessage =
        "This email address can't be used to register. If you already have an account, log in instead.";

    private const int MaxSlugAttempts = 50;

    public async Task<AuthResponse> RegisterCompanyAsync(RegisterCompanyRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        passwordPolicy.EnsureAcceptable(request.Password, email, request.FullName);
        if (await users.EmailExistsAsync(email, ct))
            throw DomainException.Conflict(EmailUnavailableMessage);

        var slug = await ChooseSlugAsync(request, ct);
        var company = new Company(request.CompanyName, slug);
        var admin = new User(company.Id, email, request.FullName, UserRole.Admin, passwordHasher.Hash(request.Password));
        companies.Add(company);
        users.Add(admin);
        var response = sessions.StartSession(admin);
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), ct);

        // Always run the (slow) hash check, even for unknown emails, so timing reveals nothing.
        var check = passwordHasher.Verify(user?.PasswordHash, request.Password);

        if (user is null || user.IsLockedOut(now))
            throw DomainException.Unauthorized(LoginFailedMessage);

        if (check == PasswordCheck.Failed)
        {
            var o = loginOptions.Value;
            user.RegisterFailedLogin(now, o.MaxFailedAttempts, o.LockoutDuration);
            await unitOfWork.SaveChangesAsync(ct);
            throw DomainException.Unauthorized(LoginFailedMessage);
        }

        if (!user.IsActive)
            throw DomainException.Unauthorized(LoginFailedMessage);

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.ChangePasswordHash(passwordHasher.Hash(request.Password)); // upgrade to current hashing settings

        user.RegisterSuccessfulLogin();
        var response = sessions.StartSession(user);
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task<UserResponse> GetCurrentUserAsync(CancellationToken ct)
    {
        var id = currentUser.UserId ?? throw DomainException.Unauthorized("Not signed in.");
        var user = (await users.GetByIdAsync(id, ct)).OrThrow(id);
        return UserResponse.From(user);
    }

    /// <summary>Uses the requested slug if given (must be free), else derives a free one from the name.</summary>
    private async Task<string> ChooseSlugAsync(RegisterCompanyRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.CompanySlug))
        {
            var requested = CompanySlug.Validate(request.CompanySlug);
            if (await companies.SlugExistsAsync(requested, ct))
                throw DomainException.Conflict($"The slug '{requested}' is already taken.");
            return requested;
        }

        var baseSlug = CompanySlug.FromName(request.CompanyName);
        if (!await companies.SlugExistsAsync(baseSlug, ct))
            return baseSlug;
        for (var n = 2; n <= MaxSlugAttempts; n++)
        {
            var candidate = CompanySlug.WithSuffix(baseSlug, n);
            if (!await companies.SlugExistsAsync(candidate, ct))
                return candidate;
        }
        throw DomainException.Conflict("Could not generate a unique slug; please choose one.");
    }
}
