using System.Linq.Expressions;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.Rooms;
using HotelManagement.Application.Users;
using HotelManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace HotelManagement.Infrastructure.Persistence;

// Read-side queries: untracked, projected straight to response DTOs in SQL.

internal static class BookingPredicates
{
    /// <summary>Bookings that occupy their room for any part of [checkIn, checkOut).</summary>
    public static Expression<Func<Booking, bool>> ActiveDuring(DateOnly checkIn, DateOnly checkOut) =>
        b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
             && b.CheckIn < checkOut
             && checkIn < b.CheckOut;
}

internal static class PagingExtensions
{
    /// <summary>Counts and pages a query. The query must already be ordered.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }

    public static string ContainsPattern(this string search) => $"%{search.Trim()}%";
}

internal class UserQueries(AppDbContext db) : IUserQueries
{
    public Task<PagedResult<UserResponse>> ListAsync(UserQuery q, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = q.Search.ContainsPattern();
            query = query.Where(u => EF.Functions.ILike(u.FullName, pattern) || EF.Functions.ILike(u.Email, pattern));
        }
        if (q.Role is { } role) query = query.Where(u => u.Role == role);

        return query
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Select(u => new UserResponse(u.Id, u.CompanyId, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt))
            .ToPagedAsync(q, ct);
    }
}

internal class HotelQueries(AppDbContext db) : IHotelQueries
{
    public Task<PagedResult<HotelResponse>> ListAsync(HotelQuery q, CancellationToken ct)
    {
        var query = db.Hotels.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = q.Search.ContainsPattern();
            query = query.Where(h => EF.Functions.ILike(h.Name, pattern) || EF.Functions.ILike(h.City, pattern));
        }
        if (!string.IsNullOrWhiteSpace(q.Country))
            query = query.Where(h => EF.Functions.ILike(h.Country, q.Country.Trim()));

        return Project(query.OrderBy(h => h.Name).ThenBy(h => h.Id)).ToPagedAsync(q, ct);
    }

    public Task<HotelResponse?> GetAsync(Guid id, CancellationToken ct) =>
        Project(db.Hotels.AsNoTracking().Where(h => h.Id == id)).FirstOrDefaultAsync(ct);

    private IQueryable<HotelResponse> Project(IQueryable<Hotel> hotels) =>
        hotels.Select(h => new HotelResponse(
            h.Id, h.Name, h.Address, h.City, h.Country, h.Phone, h.Email,
            db.Rooms.Count(r => r.HotelId == h.Id), h.CreatedAt));
}

internal class RoomQueries(AppDbContext db) : IRoomQueries
{
    private static readonly Expression<Func<Room, RoomResponse>> ToResponse = r =>
        new RoomResponse(r.Id, r.HotelId, r.Hotel.Name, r.Number, r.Type, r.Capacity, r.PricePerNight, r.Floor, r.IsActive);

    public Task<PagedResult<RoomResponse>> ListAsync(RoomQuery q, CancellationToken ct)
    {
        var query = db.Rooms.AsNoTracking();
        if (q.HotelId is { } hotelId) query = query.Where(r => r.HotelId == hotelId);
        if (q.Type is { } type) query = query.Where(r => r.Type == type);
        if (q.MinCapacity is { } cap) query = query.Where(r => r.Capacity >= cap);
        if (q.MaxPrice is { } price) query = query.Where(r => r.PricePerNight <= price);
        if (q.IsActive is { } active) query = query.Where(r => r.IsActive == active);

        return query
            .OrderBy(r => r.Hotel.Name).ThenBy(r => r.Number).ThenBy(r => r.Id)
            .Select(ToResponse)
            .ToPagedAsync(q, ct);
    }

    public Task<PagedResult<RoomResponse>> ListAvailableAsync(
        Guid hotelId, DateOnly checkIn, DateOnly checkOut, int guests, RoomType? type, PageQuery page, CancellationToken ct)
    {
        var blocking = db.Bookings.Where(BookingPredicates.ActiveDuring(checkIn, checkOut));
        var query = db.Rooms.AsNoTracking().Where(r =>
            r.HotelId == hotelId &&
            r.IsActive &&
            r.Capacity >= guests &&
            !blocking.Any(b => b.RoomId == r.Id));
        if (type is { } t) query = query.Where(r => r.Type == t);

        return query
            .OrderBy(r => r.PricePerNight).ThenBy(r => r.Number)
            .Select(ToResponse)
            .ToPagedAsync(page, ct);
    }

    public Task<RoomResponse?> GetAsync(Guid id, CancellationToken ct) =>
        db.Rooms.AsNoTracking().Where(r => r.Id == id).Select(ToResponse).FirstOrDefaultAsync(ct);
}

internal class GuestQueries(AppDbContext db) : IGuestQueries
{
    public Task<PagedResult<GuestResponse>> ListAsync(GuestQuery q, CancellationToken ct)
    {
        var query = db.Guests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = q.Search.ContainsPattern();
            query = query.Where(g =>
                EF.Functions.ILike(g.FirstName, pattern) ||
                EF.Functions.ILike(g.LastName, pattern) ||
                EF.Functions.ILike(g.FirstName + " " + g.LastName, pattern) ||
                (g.Email != null && EF.Functions.ILike(g.Email, pattern)) ||
                (g.Phone != null && EF.Functions.ILike(g.Phone, pattern)));
        }

        return query
            .OrderBy(g => g.LastName).ThenBy(g => g.FirstName).ThenBy(g => g.Id)
            .Select(g => new GuestResponse(g.Id, g.FirstName, g.LastName, g.Email, g.Phone, g.CreatedAt))
            .ToPagedAsync(q, ct);
    }
}

internal class BookingQueries(AppDbContext db) : IBookingQueries
{
    private static readonly Expression<Func<Booking, BookingResponse>> ToResponse = b => new BookingResponse(
        b.Id, b.Room.HotelId, b.Room.Hotel.Name, b.RoomId, b.Room.Number,
        b.GuestId, b.Guest.FirstName + " " + b.Guest.LastName,
        b.CheckIn, b.CheckOut, b.NumberOfGuests, b.PricePerNight, b.TotalPrice, b.Status, b.Notes,
        b.CreatedAt, b.CheckedInAt, b.CheckedOutAt, b.CancelledAt);

    public Task<PagedResult<BookingResponse>> ListAsync(BookingQuery q, CancellationToken ct)
    {
        var query = db.Bookings.AsNoTracking();
        if (q.HotelId is { } hotelId) query = query.Where(b => b.Room.HotelId == hotelId);
        if (q.RoomId is { } roomId) query = query.Where(b => b.RoomId == roomId);
        if (q.GuestId is { } guestId) query = query.Where(b => b.GuestId == guestId);
        if (q.Status is { } status) query = query.Where(b => b.Status == status);
        if (q.From is { } from) query = query.Where(b => b.CheckOut > from);
        if (q.To is { } to) query = query.Where(b => b.CheckIn < to);

        return query
            .OrderByDescending(b => b.CheckIn).ThenBy(b => b.Id)
            .Select(ToResponse)
            .ToPagedAsync(q, ct);
    }

    public Task<BookingResponse?> GetAsync(Guid id, CancellationToken ct) =>
        db.Bookings.AsNoTracking().Where(b => b.Id == id).Select(ToResponse).FirstOrDefaultAsync(ct);
}
