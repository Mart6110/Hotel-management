using HotelManagement.Domain;

namespace HotelManagement.Application.Common;

/// <summary>The authenticated caller. Implemented by the web layer.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? CompanyId { get; }
}

/// <summary>The company whose data the current operation may see. Read by the persistence layer.</summary>
public interface ITenantContext
{
    Guid? CompanyId { get; }
}

/// <summary>
/// Lets anonymous (public) flows act within one company after resolving it from the URL.
/// Separate from <see cref="ITenantContext"/> so only code that genuinely needs it can switch tenants.
/// </summary>
public interface ITenantSelector
{
    void Select(Guid companyId);
}

/// <summary>Per-request tenant: an explicitly selected company, otherwise the signed-in user's company.</summary>
public sealed class TenantContext(ICurrentUser currentUser) : ITenantContext, ITenantSelector
{
    private Guid? _selected;

    public Guid? CompanyId => _selected ?? currentUser.CompanyId;

    public void Select(Guid companyId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company id cannot be empty.", nameof(companyId));
        _selected = companyId;
    }
}

/// <summary>Generates booking references and secret guest access codes.</summary>
public interface IBookingCodeGenerator
{
    /// <summary>A short, unambiguous, unique-enough reference such as "BK-7K3P9QXM".</summary>
    string NewReference();

    /// <summary>A high-entropy secret the guest uses to manage an online booking.</summary>
    string NewAccessCode();
}

public record EmailMessage(string To, string ToName, string Subject, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Commits tracked changes atomically. Persistence errors surface as <see cref="DomainException"/>.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
    Task<ITransaction> BeginTransactionAsync(CancellationToken ct);
}

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>Correct, but hashed with weaker settings than today's: re-hash and store it.</summary>
    SuccessRehashNeeded
}

/// <summary>Salted, slow, one-way hashing for passwords and other secrets (e.g. guest access codes).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks a secret against a stored hash. When <paramref name="passwordHash"/> is <c>null</c> (unknown account)
    /// it still does the same amount of work and returns <see cref="PasswordCheck.Failed"/>, so response
    /// timing doesn't reveal whether an account exists.
    /// </summary>
    PasswordCheck Verify(string? passwordHash, string password);
}

public record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateToken(User user);
}

internal static class Found
{
    /// <summary>Returns the entity or throws a NotFound <see cref="DomainException"/>.</summary>
    public static T OrThrow<T>(this T? entity, Guid id) where T : class =>
        entity ?? throw DomainException.NotFound(typeof(T).Name, id);
}
