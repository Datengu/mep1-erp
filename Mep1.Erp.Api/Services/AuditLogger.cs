using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Services;

public sealed class AuditLogger
{
    private readonly AppDbContext _db;

    public AuditLogger(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        int? actorWorkerId,
        string actorRole,
        string actorSource,
        string action,
        string entityType,
        string entityId,
        string? summary = null)
    {
        try
        {

            var log = new AuditLog
            {
                OccurredUtc = DateTime.UtcNow,
                ActorWorkerId = actorWorkerId,
                ActorRole = actorRole,
                ActorSource = actorSource,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // swallow - audit must never block core actions
            // later: optional fallback to file logging or in-memory buffer
        }
    }
}
