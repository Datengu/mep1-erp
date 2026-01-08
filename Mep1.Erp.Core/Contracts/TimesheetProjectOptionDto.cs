namespace Mep1.Erp.Core.Contracts;

public sealed record TimesheetProjectOptionDto(
    string JobKey,
    string Label,
    string Company,
    string Category,
    bool IsRealProject
);
