namespace Mep1.Erp.Core;

public sealed class AuditLog
{
    public int Id { get; set; }

    public DateTime OccurredUtc { get; set; }

    public int? ActorWorkerId { get; set; }

    public string ActorRole { get; set; } = "";

    public string ActorSource { get; set; } = ""; // Portal / Desktop / ApiKey

    public string Action { get; set; } = "";

    public string EntityType { get; set; } = "";

    public string EntityId { get; set; } = "";

    public string? Summary { get; set; }
}
