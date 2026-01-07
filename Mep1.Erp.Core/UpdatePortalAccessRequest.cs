namespace Mep1.Erp.Core;

public sealed record UpdatePortalAccessRequest(
    string? Role,
    bool? IsActive
);
