using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Guests;

public interface IGuestRepository
{
    Task<Guest?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> HasBookingsAsync(Guid guestId, CancellationToken ct);
    void Add(Guest guest);
    void Remove(Guest guest);
}

public interface IGuestQueries
{
    Task<PagedResult<GuestResponse>> ListAsync(GuestQuery query, CancellationToken ct);
}

public interface IGuestService
{
    Task<PagedResult<GuestResponse>> ListAsync(GuestQuery query, CancellationToken ct);
    Task<GuestResponse> GetAsync(Guid id, CancellationToken ct);
    Task<GuestResponse> CreateAsync(GuestRequest request, CancellationToken ct);
    Task<GuestResponse> UpdateAsync(Guid id, GuestRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public class GuestService(IGuestRepository guests, IGuestQueries queries, IUnitOfWork unitOfWork) : IGuestService
{
    public Task<PagedResult<GuestResponse>> ListAsync(GuestQuery query, CancellationToken ct) => queries.ListAsync(query, ct);

    public async Task<GuestResponse> GetAsync(Guid id, CancellationToken ct) =>
        GuestResponse.From((await guests.GetByIdAsync(id, ct)).OrThrow(id));

    public async Task<GuestResponse> CreateAsync(GuestRequest r, CancellationToken ct)
    {
        var guest = new Guest(r.FirstName, r.LastName, r.Email, r.Phone);
        guests.Add(guest);
        await unitOfWork.SaveChangesAsync(ct);
        return GuestResponse.From(guest);
    }

    public async Task<GuestResponse> UpdateAsync(Guid id, GuestRequest r, CancellationToken ct)
    {
        var guest = (await guests.GetByIdAsync(id, ct)).OrThrow(id);
        guest.Update(r.FirstName, r.LastName, r.Email, r.Phone);
        await unitOfWork.SaveChangesAsync(ct);
        return GuestResponse.From(guest);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var guest = (await guests.GetByIdAsync(id, ct)).OrThrow(id);
        if (await guests.HasBookingsAsync(id, ct))
            throw DomainException.Conflict("The guest has bookings and cannot be deleted.");
        guests.Remove(guest);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
