using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelManagement.Infrastructure.Persistence;

/// <summary>
/// Encrypts personal data that was stored before encryption was introduced, and fills missing
/// blind indexes. Idempotent and cheap when there's nothing to do; runs at startup after migrations.
/// </summary>
internal class PersonalDataBackfill(AppDbContext db, ILogger<PersonalDataBackfill> logger)
{
    private const int BatchSize = 500;

    public async Task RunAsync(CancellationToken ct)
    {
        var guests = await FindAsync("""
            SELECT "Id" AS "Value" FROM "Guests"
            WHERE "FirstName" NOT LIKE 'enc:%' OR "LastName" NOT LIKE 'enc:%'
               OR ("Email" IS NOT NULL AND ("Email" NOT LIKE 'enc:%' OR "EmailIndex" IS NULL))
               OR ("Phone" IS NOT NULL AND "Phone" NOT LIKE 'enc:%')
            """, ct);
        var bookings = await FindAsync("""
            SELECT "Id" AS "Value" FROM "Bookings" WHERE "Notes" IS NOT NULL AND "Notes" NOT LIKE 'enc:%'
            """, ct);

        if (guests.Count + bookings.Count == 0)
            return;

        logger.LogInformation("Encrypting personal data at rest: {Guests} guest(s), {Bookings} booking note(s)",
            guests.Count, bookings.Count);
        await ReEncryptAsync(db.Guests, guests, ct);
        await ReEncryptAsync(db.Bookings, bookings, ct);
    }

    private Task<List<Guid>> FindAsync(string sql, CancellationToken ct) =>
        db.Database.SqlQueryRaw<Guid>(sql).ToListAsync(ct);

    /// <summary>Loading decrypts (legacy plaintext passes through); marking the columns modified re-saves them encrypted.</summary>
    private async Task ReEncryptAsync<TEntity>(DbSet<TEntity> set, List<Guid> ids, CancellationToken ct) where TEntity : class
    {
        foreach (var batch in ids.Chunk(BatchSize))
        {
            var entities = await set.IgnoreQueryFilters()
                .Where(e => batch.Contains(EF.Property<Guid>(e, "Id")))
                .ToListAsync(ct);
            foreach (var entity in entities)
            {
                var entry = db.Entry(entity);
                foreach (var property in PersonalDataConventions.EncryptedProperties(entry.Metadata))
                    entry.Property(property.Name).IsModified = true;
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }
}
