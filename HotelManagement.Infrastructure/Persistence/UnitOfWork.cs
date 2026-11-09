using HotelManagement.Application.Common;
using HotelManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HotelManagement.Infrastructure.Persistence;

internal class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) =>
        PersistenceErrors.TranslateAsync(() => db.SaveChangesAsync(ct));

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken ct) =>
        new EfTransaction(await db.Database.BeginTransactionAsync(ct));

    private sealed class EfTransaction(IDbContextTransaction transaction) : ITransaction
    {
        public Task CommitAsync(CancellationToken ct) =>
            PersistenceErrors.TranslateAsync(() => transaction.CommitAsync(ct));

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

/// <summary>Turns expected database failures into <see cref="DomainException"/>s, so no layer above needs to know about PostgreSQL.</summary>
internal static class PersistenceErrors
{
    public static async Task TranslateAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (Translate(e) is { } translated)
        {
            throw translated;
        }
    }

    public static DomainException? Translate(Exception e)
    {
        if (e is DbUpdateConcurrencyException)
            return DomainException.Conflict("The record was modified by someone else. Reload and retry.");

        return FindPostgresError(e)?.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation =>
                DomainException.Conflict("A record with the same unique value already exists."),
            PostgresErrorCodes.ExclusionViolation =>
                DomainException.Conflict("The room is already booked for the selected dates."),
            PostgresErrorCodes.ForeignKeyViolation =>
                DomainException.Conflict("The record is referenced by other records and cannot be changed or removed."),
            PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure =>
                DomainException.Conflict("The request conflicted with a concurrent change. Please retry."),
            _ => null,
        };
    }

    private static PostgresException? FindPostgresError(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
            if (e is PostgresException pg) return pg;
        return null;
    }
}
