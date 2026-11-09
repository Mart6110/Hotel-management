using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Hotels;
using HotelManagement.Domain;

namespace HotelManagement.Application.Rooms;

public interface IRoomRepository
{
    Task<Room?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> HasBookingsAsync(Guid roomId, CancellationToken ct);

    /// <summary>
    /// Takes a row lock on the room for the current transaction, so concurrent bookings
    /// of the same room are processed one at a time.
    /// </summary>
    Task LockForBookingAsync(Guid roomId, CancellationToken ct);

    /// <summary>IDs of active rooms of a type that fit the party and are free for the stay, cheapest first.</summary>
    Task<IReadOnlyList<Guid>> FindFreeRoomIdsAsync(
        Guid hotelId, RoomType type, DateOnly checkIn, DateOnly checkOut, int guests, CancellationToken ct);

    void Add(Room room);
    void Remove(Room room);
}

public interface IRoomQueries
{
    Task<PagedResult<RoomResponse>> ListAsync(RoomQuery query, CancellationToken ct);
    Task<PagedResult<RoomResponse>> ListAvailableAsync(Guid hotelId, DateOnly checkIn, DateOnly checkOut, int guests, RoomType? type, PageQuery page, CancellationToken ct);
    Task<RoomResponse?> GetAsync(Guid id, CancellationToken ct);
}

public interface IRoomService
{
    Task<PagedResult<RoomResponse>> ListAsync(RoomQuery query, CancellationToken ct);
    Task<PagedResult<RoomResponse>> ListAvailableAsync(AvailabilityQuery query, CancellationToken ct);
    Task<RoomResponse> GetAsync(Guid id, CancellationToken ct);
    Task<RoomResponse> CreateAsync(RoomRequest request, CancellationToken ct);
    Task<RoomResponse> UpdateAsync(Guid id, RoomRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public class RoomService(IRoomRepository rooms, IHotelRepository hotels, IRoomQueries queries, IUnitOfWork unitOfWork) : IRoomService
{
    public Task<PagedResult<RoomResponse>> ListAsync(RoomQuery query, CancellationToken ct) => queries.ListAsync(query, ct);

    public Task<PagedResult<RoomResponse>> ListAvailableAsync(AvailabilityQuery q, CancellationToken ct)
    {
        if (q.HotelId is not { } hotelId || q.CheckIn is not { } checkIn || q.CheckOut is not { } checkOut)
            throw DomainException.Validation("HotelId, CheckIn and CheckOut are required.");
        if (checkOut <= checkIn)
            throw DomainException.Validation("CheckOut must be after CheckIn.");
        return queries.ListAvailableAsync(hotelId, checkIn, checkOut, q.Guests, q.Type, q, ct);
    }

    public async Task<RoomResponse> GetAsync(Guid id, CancellationToken ct) =>
        (await queries.GetAsync(id, ct)) ?? throw DomainException.NotFound(nameof(Room), id);

    public async Task<RoomResponse> CreateAsync(RoomRequest r, CancellationToken ct)
    {
        var hotel = (await hotels.GetByIdAsync(r.HotelId, ct)).OrThrow(r.HotelId);
        var room = new Room(hotel, r.Number, r.Type, r.Capacity, r.PricePerNight, r.Floor, r.IsActive);
        rooms.Add(room);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(room.Id, ct);
    }

    public async Task<RoomResponse> UpdateAsync(Guid id, RoomRequest r, CancellationToken ct)
    {
        var room = (await rooms.GetByIdAsync(id, ct)).OrThrow(id);
        var hotel = (await hotels.GetByIdAsync(r.HotelId, ct)).OrThrow(r.HotelId);
        room.Update(hotel, r.Number, r.Type, r.Capacity, r.PricePerNight, r.Floor, r.IsActive);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var room = (await rooms.GetByIdAsync(id, ct)).OrThrow(id);
        if (await rooms.HasBookingsAsync(id, ct))
            throw DomainException.Conflict("The room has bookings. Deactivate it instead of deleting it.");
        rooms.Remove(room);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
