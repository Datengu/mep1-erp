namespace Mep1.Erp.Core.Contracts;

public sealed record TimesheetEntryEditDto(
    int Id,
    int WorkerId,
    string JobKey,
    DateTime Date,
    decimal Hours,
    string Code,
    string? TaskDescription,
    string? CcfRef,
    string WorkType,
    List<string> Levels,
    List<string> Areas
);