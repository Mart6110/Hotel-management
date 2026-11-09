using HotelManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelManagement.Infrastructure.Persistence;

// One configuration class per entity; picked up by ApplyConfigurationsFromAssembly.

internal class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> e)
    {
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Slug).HasMaxLength(CompanySlug.MaxLength);
        e.HasIndex(x => x.Slug).IsUnique();
    }
}

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.HasIndex(x => x.Email).IsUnique();
        e.Property(x => x.Email).HasMaxLength(256);
        e.Property(x => x.FullName).HasMaxLength(200);
        e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.SecurityStamp).HasMaxLength(64);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => x.UserId);
        e.HasIndex(x => x.FamilyId);
        e.Property(x => x.TokenHash).HasMaxLength(64);
        e.Property(x => x.RevocationReason).HasMaxLength(100);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class HotelConfiguration : IEntityTypeConfiguration<Hotel>
{
    public void Configure(EntityTypeBuilder<Hotel> e)
    {
        e.HasIndex(x => x.CompanyId);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Address).HasMaxLength(300);
        e.Property(x => x.City).HasMaxLength(100);
        e.Property(x => x.Country).HasMaxLength(100);
        e.Property(x => x.Phone).HasMaxLength(50);
        e.Property(x => x.Email).HasMaxLength(256);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> e)
    {
        e.HasIndex(x => new { x.HotelId, x.Number }).IsUnique();
        e.HasIndex(x => x.CompanyId);
        e.Property(x => x.Number).HasMaxLength(20);
        e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.PricePerNight).HasPrecision(10, 2);
        e.HasOne(x => x.Hotel).WithMany().HasForeignKey(x => x.HotelId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class GuestConfiguration : IEntityTypeConfiguration<Guest>
{
    public void Configure(EntityTypeBuilder<Guest> e)
    {
        // Personal data is encrypted at rest. Emails stay matchable through a keyed-hash blind index,
        // which also enforces "one guest per email per company".
        e.Property(x => x.FirstName).IsEncrypted();
        e.Property(x => x.LastName).IsEncrypted();
        e.Property(x => x.Email).IsEncrypted();
        e.Property(x => x.Phone).IsEncrypted();
        e.HasBlindIndex(nameof(Guest.Email), GuestColumns.EmailIndex);
        e.HasIndex(nameof(Guest.CompanyId), GuestColumns.EmailIndex).IsUnique();
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> e)
    {
        e.HasIndex(x => new { x.CompanyId, x.CheckIn });
        e.HasIndex(x => x.RoomId);
        e.HasIndex(x => x.GuestId);
        e.HasIndex(x => x.Reference).IsUnique();
        e.Property(x => x.Reference).HasMaxLength(20);
        e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.AccessCodeHash).HasMaxLength(200);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.PricePerNight).HasPrecision(10, 2);
        e.Property(x => x.TotalPrice).HasPrecision(12, 2);
        e.Property(x => x.Notes).IsEncrypted(); // free text: may contain personal data
        e.Property(x => x.Version).IsRowVersion();
        e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Guest).WithMany().HasForeignKey(x => x.GuestId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.HasIndex(x => x.BookingId);
        e.Property(x => x.Amount).HasPrecision(12, 2);
        e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.Reference).HasMaxLength(200);
    }
}

/// <summary>Shadow columns that exist only in the database mapping, not on the domain entities.</summary>
internal static class GuestColumns
{
    /// <summary>HMAC blind index of the (encrypted) email, for exact-match lookups.</summary>
    public const string EmailIndex = "EmailIndex";
}
