namespace Mep1.Erp.Core;

public sealed record TimesheetLoginResultDto(
    int WorkerId,
    string WorkerName,
    string Initials
);
