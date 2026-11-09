using HotelManagement.Domain;

namespace HotelManagement.Application.Common;

/// <summary>The authenticated caller. Implemented by the web layer.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? CompanyId { get; }
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

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
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
