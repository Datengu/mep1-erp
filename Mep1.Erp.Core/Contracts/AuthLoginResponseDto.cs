namespace Mep1.Erp.Core.Contracts;
public sealed record AuthLoginResponseDto(
    int WorkerId,
    string Username,
    string Role,
    string Name,
    string Initials,
    bool MustChangePassword,
    string AccessToken,
    DateTime ExpiresUtc
);