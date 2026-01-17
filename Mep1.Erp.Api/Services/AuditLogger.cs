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
        int? subjectWorkerId,
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
                SubjectWorkerId = subjectWorkerId,
                IsOnBehalf = actorWorkerId.HasValue
                             && subjectWorkerId.HasValue
                             && actorWorkerId.Value != subjectWorkerId.Value,
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
        }
    }
}
