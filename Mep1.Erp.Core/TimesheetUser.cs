namespace Mep1.Erp.Core;

public enum TimesheetUserRole
{
    Worker = 0,
    Admin = 1,
    Owner = 2
}

public sealed class TimesheetUser
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    public string UsernameNormalized { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    // Link to your existing People/Worker record.
    public int WorkerId { get; set; }

    public bool IsActive { get; set; } = true;

    public TimesheetUserRole Role { get; set; } = TimesheetUserRole.Worker;

    public bool MustChangePassword { get; set; } = false;

    public DateTime? PasswordChangedAtUtc { get; set; }

}
