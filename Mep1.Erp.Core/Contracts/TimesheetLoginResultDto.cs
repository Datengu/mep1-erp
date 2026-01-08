namespace Mep1.Erp.Core.Contracts;

public sealed record TimesheetLoginResultDto(
    int WorkerId,
    string WorkerName,
    string Initials
);
