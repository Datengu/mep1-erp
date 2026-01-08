namespace Mep1.Erp.Core;

public sealed record CreatePortalAccessResultDto(
    PortalAccessDto PortalAccess,
    string TemporaryPassword
);
