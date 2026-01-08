using Mep1.Erp.Core;

namespace Mep1.Erp.Api.Security;

public sealed class ActorContext
{
    public int WorkerId { get; init; }
    public TimesheetUserRole Role { get; init; }
    public string Username { get; init; } = "";
    public DateTime ExpiresUtc { get; init; }

    public bool IsAdminOrOwner => Role == TimesheetUserRole.Admin || Role == TimesheetUserRole.Owner;
}