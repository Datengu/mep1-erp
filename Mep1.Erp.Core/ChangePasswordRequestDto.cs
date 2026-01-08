namespace Mep1.Erp.Core;
public sealed record ChangePasswordRequestDto(
    string Username,
    string CurrentPassword,
    string NewPassword
);
