namespace Mep1.Erp.TimesheetWeb.Models;

public sealed class TimesheetLoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class TimesheetLoginResponse
{
    public int WorkerId { get; set; }
    public string Name { get; set; } = "";
    public string Initials { get; set; } = "";
}
