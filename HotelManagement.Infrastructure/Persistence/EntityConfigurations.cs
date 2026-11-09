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
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
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
        e.HasIndex(x => new { x.CompanyId, x.Email }).IsUnique();
        e.Property(x => x.FirstName).HasMaxLength(100);
        e.Property(x => x.LastName).HasMaxLength(100);
        e.Property(x => x.Email).HasMaxLength(256);
        e.Property(x => x.Phone).HasMaxLength(50);
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
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.PricePerNight).HasPrecision(10, 2);
        e.Property(x => x.TotalPrice).HasPrecision(12, 2);
        e.Property(x => x.Notes).HasMaxLength(1000);
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
