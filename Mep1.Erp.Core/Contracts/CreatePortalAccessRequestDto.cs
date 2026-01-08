namespace Mep1.Erp.Core.Contracts;

public sealed record CreatePortalAccessRequestDto(
    string Username,
    string Role // "Worker" | "Admin" | "Owner"
);
