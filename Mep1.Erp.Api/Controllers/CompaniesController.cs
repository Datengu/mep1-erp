using Mep1.Erp.Api.Services;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CompaniesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;

    public CompaniesController(AppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<List<CompanyListItemDto>> Get()
    {
        return await _db.Companies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CompanyListItemDto
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                IsActive = c.IsActive
            })
            .ToListAsync();
    }
}
