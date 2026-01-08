using System;

namespace Mep1.Erp.Core.Contracts;

public sealed record CreateTimesheetEntryDto(
    int WorkerId,
    string JobKey,
    DateTime Date,
    decimal Hours,
    string Code,
    string? CcfRef,
    string? TaskDescription,
    string WorkType,
    List<string> Levels,
    List<string> Areas
);
