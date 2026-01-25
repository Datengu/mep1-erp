using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOrOwner")]
public sealed class CompaniesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;

    public CompaniesController(AppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    private (int WorkerId, string Role, string Source) GetActorForAudit()
    {
        var id = ClaimsActor.GetWorkerId(User);
        var role = ClaimsActor.GetRole(User);
        return (id, role.ToString(), GetClientApp());
    }

    private string GetClientApp()
    {
        var kind = HttpContext.Items["ApiKeyKind"] as string;
        return string.Equals(kind, "Admin", StringComparison.Ordinal) ? "Desktop" : "Portal";
    }

    [HttpGet]
    public async Task<ActionResult<List<CompanyListItemDto>>> Get()
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
