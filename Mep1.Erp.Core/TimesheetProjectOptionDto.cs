namespace Mep1.Erp.Core;

public sealed record TimesheetProjectOptionDto(
    string JobKey,
    string Label,
    string Company,
    string Category,
    bool IsRealProject
);
