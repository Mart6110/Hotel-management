using HotelManagement.Application.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Companies;

/// <summary>The signed-in user's own company profile.</summary>
public interface ICompanyService
{
    Task<CompanyResponse> GetCurrentAsync(CancellationToken ct);
    Task<CompanyResponse> UpdateCurrentAsync(UpdateCompanyRequest request, CancellationToken ct);
}

public class CompanyService(ICompanyRepository companies, ICurrentUser currentUser, IUnitOfWork unitOfWork) : ICompanyService
{
    public async Task<CompanyResponse> GetCurrentAsync(CancellationToken ct) =>
        CompanyResponse.From(await LoadCurrentAsync(ct));

    public async Task<CompanyResponse> UpdateCurrentAsync(UpdateCompanyRequest request, CancellationToken ct)
    {
        var company = await LoadCurrentAsync(ct);
        var slug = CompanySlug.Validate(request.Slug);
        if (slug != company.Slug && await companies.SlugExistsAsync(slug, ct))
            throw DomainException.Conflict($"The slug '{slug}' is already taken.");

        company.Rename(request.Name);
        company.ChangeSlug(slug);
        await unitOfWork.SaveChangesAsync(ct);
        return CompanyResponse.From(company);
    }

    private async Task<Company> LoadCurrentAsync(CancellationToken ct)
    {
        var id = currentUser.CompanyId ?? throw DomainException.Unauthorized("Not signed in.");
        return (await companies.GetByIdAsync(id, ct)).OrThrow(id);
    }
}
