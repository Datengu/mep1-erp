namespace Mep1.Erp.Core.Contracts;
public sealed record DesktopAdminLoginResponseDto(
    int ActorWorkerId,
    string Username,
    string Role,
    string Name,
    string Initials,
    bool MustChangePassword,
    string ActorToken,
    DateTime ExpiresUtc
);