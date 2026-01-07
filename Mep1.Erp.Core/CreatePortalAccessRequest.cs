namespace Mep1.Erp.Core;

public sealed record CreatePortalAccessRequest(
    string Username,
    string Role // "Worker" | "Admin" | "Owner"
);
