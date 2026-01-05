namespace Mep1.Erp.Core;

public sealed record UpdateTimesheetEntryDto(
    int WorkerId,
    string JobKey,
    DateTime Date,
    decimal Hours,
    string Code,
    string? TaskDescription,
    string? CcfRef
);