using System.Security.Claims;
using Mep1.Erp.Core;

namespace Mep1.Erp.Api.Security;

public static class ClaimsActor
{
    public static int GetWorkerId(ClaimsPrincipal user)
    {
        var s = user.FindFirstValue("wid") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(s, out var id) || id <= 0)
            throw new InvalidOperationException("Missing/invalid worker id claim.");
        return id;
    }

    public static TimesheetUserRole GetRole(ClaimsPrincipal user)
    {
        var s = user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role) ?? "Worker";
        if (!Enum.TryParse<TimesheetUserRole>(s, ignoreCase: true, out var role))
            role = TimesheetUserRole.Worker;
        return role;
    }

    public static string GetUsername(ClaimsPrincipal user)
        => user.FindFirstValue("usr") ?? user.FindFirstValue(ClaimTypes.Name) ?? "";
}
