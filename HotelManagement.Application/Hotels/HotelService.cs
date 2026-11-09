using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Hotels;

public interface IHotelRepository
{
    Task<Hotel?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> HasRoomsAsync(Guid hotelId, CancellationToken ct);
    void Add(Hotel hotel);
    void Remove(Hotel hotel);
}

public interface IHotelQueries
{
    Task<PagedResult<HotelResponse>> ListAsync(HotelQuery query, CancellationToken ct);
    Task<HotelResponse?> GetAsync(Guid id, CancellationToken ct);
}

public interface IHotelService
{
    Task<PagedResult<HotelResponse>> ListAsync(HotelQuery query, CancellationToken ct);
    Task<HotelResponse> GetAsync(Guid id, CancellationToken ct);
    Task<HotelResponse> CreateAsync(HotelRequest request, CancellationToken ct);
    Task<HotelResponse> UpdateAsync(Guid id, HotelRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public class HotelService(IHotelRepository hotels, IHotelQueries queries, IUnitOfWork unitOfWork) : IHotelService
{
    public Task<PagedResult<HotelResponse>> ListAsync(HotelQuery query, CancellationToken ct) => queries.ListAsync(query, ct);

    public async Task<HotelResponse> GetAsync(Guid id, CancellationToken ct) =>
        (await queries.GetAsync(id, ct)) ?? throw DomainException.NotFound(nameof(Hotel), id);

    public async Task<HotelResponse> CreateAsync(HotelRequest r, CancellationToken ct)
    {
        var hotel = new Hotel(r.Name, r.Address, r.City, r.Country, r.Phone, r.Email);
        hotels.Add(hotel);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(hotel.Id, ct);
    }

    public async Task<HotelResponse> UpdateAsync(Guid id, HotelRequest r, CancellationToken ct)
    {
        var hotel = (await hotels.GetByIdAsync(id, ct)).OrThrow(id);
        hotel.Update(r.Name, r.Address, r.City, r.Country, r.Phone, r.Email);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var hotel = (await hotels.GetByIdAsync(id, ct)).OrThrow(id);
        if (await hotels.HasRoomsAsync(id, ct))
            throw DomainException.Conflict("Remove the hotel's rooms before deleting it.");
        hotels.Remove(hotel);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
