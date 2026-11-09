using HotelManagement.Application.Auth;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.Rooms;
using HotelManagement.Application.Users;
using HotelManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace HotelManagement.Infrastructure.Persistence;

// Write-side repositories: load and track aggregates. All lookups go through tenant
// query filters, so an ID from another company simply isn't found.

internal class CompanyRepository(AppDbContext db) : ICompanyRepository
{
    public void Add(Company company) => db.Companies.Add(company);
}

internal class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalizedEmail, ct);

    public void Add(User user) => db.Users.Add(user);
}

internal class HotelRepository(AppDbContext db) : IHotelRepository
{
    public Task<Hotel?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Hotels.FirstOrDefaultAsync(h => h.Id == id, ct);

    public Task<bool> HasRoomsAsync(Guid hotelId, CancellationToken ct) =>
        db.Rooms.AnyAsync(r => r.HotelId == hotelId, ct);

    public void Add(Hotel hotel) => db.Hotels.Add(hotel);
    public void Remove(Hotel hotel) => db.Hotels.Remove(hotel);
}

internal class RoomRepository(AppDbContext db) : IRoomRepository
{
    public Task<Room?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<bool> HasBookingsAsync(Guid roomId, CancellationToken ct) =>
        db.Bookings.AnyAsync(b => b.RoomId == roomId, ct);

    public Task LockForBookingAsync(Guid roomId, CancellationToken ct) =>
        PersistenceErrors.TranslateAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Rooms" WHERE "Id" = {roomId} FOR UPDATE""", ct));

    public void Add(Room room) => db.Rooms.Add(room);
    public void Remove(Room room) => db.Rooms.Remove(room);
}

internal class GuestRepository(AppDbContext db) : IGuestRepository
{
    public Task<Guest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Guests.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<bool> HasBookingsAsync(Guid guestId, CancellationToken ct) =>
        db.Bookings.AnyAsync(b => b.GuestId == guestId, ct);

    public void Add(Guest guest) => db.Guests.Add(guest);
    public void Remove(Guest guest) => db.Guests.Remove(guest);
}

internal class BookingRepository(AppDbContext db) : IBookingRepository
{
    public Task<Booking?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Bookings.Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<Booking?> GetWithDetailsAsync(Guid id, CancellationToken ct) =>
        db.Bookings
            .AsNoTracking()
            .Include(b => b.Room).ThenInclude(r => r.Hotel)
            .Include(b => b.Guest)
            .Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<bool> HasOverlapAsync(Guid roomId, DateOnly checkIn, DateOnly checkOut, CancellationToken ct) =>
        db.Bookings.Where(BookingPredicates.ActiveDuring(checkIn, checkOut)).AnyAsync(b => b.RoomId == roomId, ct);

    public Task LockAsync(Guid bookingId, CancellationToken ct) =>
        PersistenceErrors.TranslateAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Bookings" WHERE "Id" = {bookingId} FOR UPDATE""", ct));

    public void Add(Booking booking) => db.Bookings.Add(booking);
}
