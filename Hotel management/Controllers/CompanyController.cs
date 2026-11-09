using HotelManagement.Api.Auth;
using HotelManagement.Application.Companies;
using HotelManagement.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Controllers;

/// <summary>The signed-in user's company, including its public booking slug.</summary>
[ApiController]
[Route("api/company")]
public class CompanyController(ICompanyService companies) : ControllerBase
{
    [HttpGet]
    public Task<CompanyResponse> Get(CancellationToken ct) => companies.GetCurrentAsync(ct);

    /// <summary>Renames the company or changes its slug. Changing the slug changes the public booking URLs.</summary>
    [HttpPut]
    [Authorize(Roles = Roles.Admin)]
    public Task<CompanyResponse> Update(UpdateCompanyRequest request, CancellationToken ct) =>
        companies.UpdateCurrentAsync(request, ct);
}
