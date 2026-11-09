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
    public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Company?> FindBySlugAsync(string slug, CancellationToken ct) =>
        db.Companies.FirstOrDefaultAsync(c => c.Slug == slug, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        db.Companies.AnyAsync(c => c.Slug == slug, ct);

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

    public Task<User?> FindByIdAcrossTenantsAsync(Guid id, CancellationToken ct) =>
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<UserSessionState?> GetSessionStateAsync(Guid id, CancellationToken ct) =>
        db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserSessionState(u.IsActive, u.SecurityStamp))
            .FirstOrDefaultAsync(ct);

    public void Add(User user) => db.Users.Add(user);
}

internal class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    // Refresh requests are anonymous (no tenant yet), so these lookups ignore the tenant filter.
    // That's safe: a token is only found by its 256-bit secret, or by ids taken from such a token.

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveInFamilyAsync(Guid familyId, CancellationToken ct) =>
        await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveForUserAsync(Guid userId, CancellationToken ct) =>
        await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
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

    public async Task<IReadOnlyList<Guid>> FindFreeRoomIdsAsync(
        Guid hotelId, RoomType type, DateOnly checkIn, DateOnly checkOut, int guests, CancellationToken ct)
    {
        var blocking = db.Bookings.Where(BookingPredicates.ActiveDuring(checkIn, checkOut));
        return await db.Rooms
            .Where(r => r.HotelId == hotelId && r.Type == type && r.IsActive && r.Capacity >= guests
                        && !blocking.Any(b => b.RoomId == r.Id))
            .OrderBy(r => r.PricePerNight).ThenBy(r => r.Capacity).ThenBy(r => r.Number)
            .Select(r => r.Id)
            .ToListAsync(ct);
    }

    public void Add(Room room) => db.Rooms.Add(room);
    public void Remove(Room room) => db.Rooms.Remove(room);
}

internal class GuestRepository(AppDbContext db) : IGuestRepository
{
    public Task<Guest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Guests.FirstOrDefaultAsync(g => g.Id == id, ct);

    /// <summary>Emails are encrypted (randomized), so they're matched through their blind index instead.</summary>
    public Task<Guest?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        var index = db.BlindIndexOf(normalizedEmail);
        return db.Guests.FirstOrDefaultAsync(g => EF.Property<string>(g, GuestColumns.EmailIndex) == index, ct);
    }

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

    public async Task<IReadOnlyList<Booking>> ListForGuestAsync(Guid guestId, CancellationToken ct) =>
        await db.Bookings
            .Include(b => b.Room).ThenInclude(r => r.Hotel)
            .Include(b => b.Payments)
            .Where(b => b.GuestId == guestId)
            .ToListAsync(ct);

    public Task<Booking?> FindByReferenceAsync(string reference, CancellationToken ct) =>
        db.Bookings
            .Include(b => b.Room).ThenInclude(r => r.Hotel)
            .Include(b => b.Guest)
            .FirstOrDefaultAsync(b => b.Reference == reference, ct);

    public Task<bool> HasOverlapAsync(Guid roomId, DateOnly checkIn, DateOnly checkOut, CancellationToken ct) =>
        db.Bookings.Where(BookingPredicates.ActiveDuring(checkIn, checkOut)).AnyAsync(b => b.RoomId == roomId, ct);

    public Task LockAsync(Guid bookingId, CancellationToken ct) =>
        PersistenceErrors.TranslateAsync(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Bookings" WHERE "Id" = {bookingId} FOR UPDATE""", ct));

    public void Add(Booking booking) => db.Bookings.Add(booking);
}
