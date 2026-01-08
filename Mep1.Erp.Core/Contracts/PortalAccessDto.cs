namespace Mep1.Erp.Core.Contracts;

public sealed record PortalAccessDto(
    bool Exists,
    int WorkerId,
    string? Username,
    string? Role,
    bool IsActive,
    bool MustChangePassword,
    DateTime? PasswordChangedAtUtc
);
