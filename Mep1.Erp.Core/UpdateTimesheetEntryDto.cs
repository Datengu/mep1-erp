using System;

namespace Mep1.Erp.Core;

public sealed record UpdateTimesheetEntryDto(
    int WorkerId,
    DateTime Date,
    decimal Hours,
    string Code,
    int ProjectId,
    string? TaskDescription,
    string? CcfRef
);
