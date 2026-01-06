namespace Mep1.Erp.Core;
public sealed record ChangePasswordRequest(
    string Username,
    string CurrentPassword,
    string NewPassword
);
