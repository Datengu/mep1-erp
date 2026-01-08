namespace Mep1.Erp.Core.Contracts;
public sealed record ChangePasswordRequestDto(
    string Username,
    string CurrentPassword,
    string NewPassword
);
