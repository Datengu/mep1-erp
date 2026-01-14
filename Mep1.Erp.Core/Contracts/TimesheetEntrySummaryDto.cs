using System;

namespace Mep1.Erp.Core.Contracts;

public sealed record TimesheetEntrySummaryDto(
    int Id,
    int EntryId,
    DateTime Date,
    decimal Hours,
    string Code,
    string JobKey,
    string? ProjectCompanyCode,
    string ProjectCategory,
    bool IsRealProject,
    string TaskDescription,
    string CcfRef,
    string WorkType,
    List<string> Levels,
    List<string> Areas
);
