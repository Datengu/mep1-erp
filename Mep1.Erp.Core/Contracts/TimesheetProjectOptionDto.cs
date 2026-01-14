namespace Mep1.Erp.Core.Contracts;

public sealed record TimesheetProjectOptionDto(
    string JobKey,
    string Label,
    int? CompanyId,
    string Category,
    bool IsRealProject
);
