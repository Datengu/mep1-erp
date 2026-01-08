namespace Mep1.Erp.Core.Contracts;

public sealed record CreatePortalAccessResultDto(
    PortalAccessDto PortalAccess,
    string TemporaryPassword
);
