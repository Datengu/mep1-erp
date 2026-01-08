namespace Mep1.Erp.Core.Contracts;

public sealed record UpdatePortalAccessRequestDto(
    string? Role,
    bool? IsActive
);
