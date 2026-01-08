namespace Mep1.Erp.Core;

public sealed record CreatePortalAccessRequestDto(
    string Username,
    string Role // "Worker" | "Admin" | "Owner"
);
