namespace Mep1.Erp.Core;

public sealed record CreatePortalAccessResult(
    PortalAccessDto PortalAccess,
    string TemporaryPassword
);
