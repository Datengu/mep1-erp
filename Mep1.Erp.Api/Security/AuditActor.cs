using Mep1.Erp.Core;

namespace Mep1.Erp.Api.Security;

public static class AuditActor
{
    public static (int actorWorkerId, string actorRole, string actorSource) FromJwt(HttpContext ctx)
    {
        var id = ClaimsActor.GetWorkerId(ctx.User);
        var role = ClaimsActor.GetRole(ctx.User);
        return (id, role.ToString(), "Jwt");
    }
}
