namespace Mep1.Erp.Core;

public sealed class RefreshToken
{
    public int Id { get; set; }

    public int TimesheetUserId { get; set; }
    public TimesheetUser TimesheetUser { get; set; } = null!;

    // store hash ONLY (never raw token)
    public string TokenHash { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedUtc == null && DateTime.UtcNow < ExpiresUtc;
}
