using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = "AdminOrOwner")]
public sealed class AuditController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditController(AppDbContext db)
    {
        _db = db;
    }

    private bool IsAdminKey()
        => string.Equals(HttpContext.Items["ApiKeyKind"] as string, "Admin", StringComparison.Ordinal);

    private ActionResult? RequireAdminKey()
    {
        if (IsAdminKey()) return null;
        return Unauthorized("Admin API key required.");
    }

    [HttpGet]
    public async Task<ActionResult<List<AuditLogRowDto>>> Get(
        [FromQuery] int take = 200,
        [FromQuery] int skip = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] int? actorWorkerId = null,
        [FromQuery] string? action = null)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        take = Math.Clamp(take, 1, 1000);
        skip = Math.Max(0, skip);

        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
            q = q.Where(x => x.EntityType == entityType.Trim());

        if (!string.IsNullOrWhiteSpace(entityId))
            q = q.Where(x => x.EntityId == entityId.Trim());

        if (actorWorkerId.HasValue)
            q = q.Where(x => x.ActorWorkerId == actorWorkerId.Value);

        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(x => x.Action == action.Trim());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.Summary != null && x.Summary.Contains(s)) ||
                x.Action.Contains(s) ||
                x.EntityType.Contains(s) ||
                x.EntityId.Contains(s) ||
                x.ActorRole.Contains(s) ||
                x.ActorSource.Contains(s));
        }

        var rows = await q
            .OrderByDescending(x => x.OccurredUtc)
            .Skip(skip)
            .Take(take)
            .Select(x => new AuditLogRowDto
            {
                Id = x.Id,
                OccurredUtc = x.OccurredUtc,
                ActorWorkerId = x.ActorWorkerId,
                ActorRole = x.ActorRole,
                ActorSource = x.ActorSource,
                Action = x.Action,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                Summary = x.Summary
            })
            .ToListAsync();

        return Ok(rows);
    }
}
