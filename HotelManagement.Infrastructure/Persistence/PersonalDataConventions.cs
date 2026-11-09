using HotelManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HotelManagement.Infrastructure.Persistence;

/// <summary>
/// Declarative personal-data protection for entity mappings:
/// <c>.IsEncrypted()</c> marks a column for encryption at rest, and
/// <c>HasBlindIndex(...)</c> adds a searchable keyed hash of an encrypted column.
/// <see cref="AppDbContext"/> applies them, so a new sensitive column needs one line, not new plumbing.
/// </summary>
public static class PersonalDataConventions
{
    private const string EncryptedAnnotation = "HotelManagement:Encrypted";
    private const string BlindIndexSourceAnnotation = "HotelManagement:BlindIndexSource";

    /// <summary>Encrypt this string column at rest. Ciphertext is longer than the value, so the column becomes unbounded text.</summary>
    public static PropertyBuilder<TProperty> IsEncrypted<TProperty>(this PropertyBuilder<TProperty> property)
    {
        if (typeof(TProperty) != typeof(string))
            throw new InvalidOperationException("Only string properties can be encrypted.");
        property.Metadata.SetMaxLength(null);
        return property.HasAnnotation(EncryptedAnnotation, true);
    }

    /// <summary>Adds a shadow column holding <see cref="IBlindIndex"/> of <paramref name="sourceProperty"/>, kept up to date on save.</summary>
    public static PropertyBuilder<string?> HasBlindIndex<TEntity>(
        this EntityTypeBuilder<TEntity> entity, string sourceProperty, string indexColumn) where TEntity : class =>
        entity.Property<string?>(indexColumn).HasMaxLength(64).HasAnnotation(BlindIndexSourceAnnotation, sourceProperty);

    /// <summary>Called from OnModelCreating: attaches encrypting value converters to every marked column.</summary>
    public static void ApplyEncryption(this ModelBuilder modelBuilder, IFieldEncryptor encryptor)
    {
        var converter = new ValueConverter<string, string>(v => encryptor.Encrypt(v), v => encryptor.Decrypt(v));
        foreach (var property in EncryptedProperties(modelBuilder.Model))
            property.SetValueConverter(converter);
    }

    public static IEnumerable<IMutableProperty> EncryptedProperties(IMutableModel model) =>
        model.GetEntityTypes().SelectMany(t => t.GetProperties())
            .Where(p => p.FindAnnotation(EncryptedAnnotation)?.Value is true);

    public static IEnumerable<IProperty> EncryptedProperties(IEntityType entityType) =>
        entityType.GetProperties().Where(p => p.FindAnnotation(EncryptedAnnotation)?.Value is true);

    /// <summary>Called before saving: recomputes blind indexes for added/modified rows.</summary>
    public static void UpdateBlindIndexes(this ChangeTracker changeTracker, IBlindIndex blindIndex)
    {
        foreach (var entry in changeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var indexProperty in entry.Metadata.GetProperties())
            {
                if (indexProperty.FindAnnotation(BlindIndexSourceAnnotation)?.Value is not string source)
                    continue;
                var value = entry.Property(source).CurrentValue as string;
                var hash = string.IsNullOrWhiteSpace(value) ? null : blindIndex.Compute(value);
                var indexEntry = entry.Property(indexProperty.Name);
                if (!Equals(indexEntry.CurrentValue, hash))
                    indexEntry.CurrentValue = hash;
            }
        }
    }
}
