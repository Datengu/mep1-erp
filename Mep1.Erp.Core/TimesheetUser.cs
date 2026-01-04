namespace Mep1.Erp.Core;

public sealed class TimesheetUser
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    // Link to your existing People/Worker record.
    public int WorkerId { get; set; }

    public bool IsActive { get; set; } = true;
}
