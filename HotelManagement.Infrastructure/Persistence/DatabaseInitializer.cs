using HotelManagement.Application.Common;
using HotelManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HotelManagement.Infrastructure.Persistence;

public class DatabaseOptions
{
    public bool MigrateOnStartup { get; set; } = true;
    public bool SeedSampleData { get; set; }
}

public interface IDatabaseInitializer
{
    /// <summary>Applies pending migrations and, when enabled, seeds demo data into an empty database.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}

internal class DatabaseInitializer(
    AppDbContext db,
    DemoDataSeeder seeder,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (options.Value.MigrateOnStartup)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);
        }

        if (options.Value.SeedSampleData && !await db.Companies.AnyAsync(ct))
        {
            logger.LogInformation("Seeding sample data");
            await seeder.SeedAsync(ct);
        }
    }
}

/// <summary>Creates a demo company with staff, hotels, rooms, guests and bookings.</summary>
internal class DemoDataSeeder(AppDbContext db, IPasswordHasher passwordHasher, TimeProvider clock)
{
    public const string DemoPassword = "Passw0rd!";

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var company = new Company("Demo Hotels Group");
        var hash = passwordHasher.Hash(DemoPassword);

        // No signed-in user while seeding, so tenant ownership is assigned explicitly.
        T Own<T>(T entity) where T : TenantEntity
        {
            entity.AssignCompany(company.Id);
            return entity;
        }

        var harbour = Own(new Hotel("Harbour View Hotel", "1 Quay Street", "Copenhagen", "Denmark", "+45 1234 5678", "info@harbourview.example"));
        var alpine = Own(new Hotel("Alpine Lodge", "12 Bergstrasse", "Innsbruck", "Austria", "+43 512 000 000", "stay@alpinelodge.example"));

        var rooms = new List<Room>();
        void AddRooms(Hotel hotel, int floors, (RoomType Type, int Capacity, decimal Price)[] layout)
        {
            for (var floor = 1; floor <= floors; floor++)
                for (var i = 0; i < layout.Length; i++)
                {
                    var (type, capacity, price) = layout[i];
                    rooms.Add(new Room(hotel, $"{floor}{i + 1:00}", type, capacity, price + floor * 5, floor));
                }
        }
        AddRooms(harbour, 3, [(RoomType.Single, 1, 89), (RoomType.Double, 2, 129), (RoomType.Twin, 2, 129), (RoomType.Suite, 4, 299)]);
        AddRooms(alpine, 2, [(RoomType.Double, 2, 149), (RoomType.Family, 5, 219), (RoomType.Suite, 4, 349)]);

        var guests = new[]
        {
            Own(new Guest("Anna", "Jensen", "anna.jensen@example.com", "+45 2000 0001")),
            Own(new Guest("Lukas", "Gruber", "lukas.gruber@example.com", "+43 660 000 0002")),
            Own(new Guest("Maria", "Rossi", "maria.rossi@example.com", null)),
            Own(new Guest("John", "Smith", null, "+44 7700 900003")),
        };

        var current = Booking.Create(rooms[1], guests[0], today, today.AddDays(3), 2, null, now);
        current.MarkCheckedIn(now);
        current.RecordPayment(current.PricePerNight, PaymentMethod.Card, "Deposit", now, null);

        db.Companies.Add(company);
        db.Users.AddRange(
            new User(company.Id, "admin@demo.local", "Demo Admin", UserRole.Admin, hash),
            new User(company.Id, "manager@demo.local", "Demo Manager", UserRole.Manager, hash),
            new User(company.Id, "reception@demo.local", "Demo Receptionist", UserRole.Receptionist, hash));
        db.Hotels.AddRange(harbour, alpine);
        db.Rooms.AddRange(rooms);
        db.Guests.AddRange(guests);
        db.Bookings.AddRange(
            current,
            Booking.Create(rooms[3], guests[2], today.AddDays(7), today.AddDays(11), 3, null, now),
            Booking.Create(rooms[13], guests[1], today.AddDays(14), today.AddDays(19), 2, null, now));
        await db.SaveChangesAsync(ct);
    }
}
