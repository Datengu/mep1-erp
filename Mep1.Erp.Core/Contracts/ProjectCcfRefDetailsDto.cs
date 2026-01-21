namespace Mep1.Erp.Core.Contracts;
public record ProjectCcfRefDetailsDto(
    int Id,
    string Code,
    bool IsActive,
    decimal? EstimatedValue,
    decimal? QuotedValue,
    DateTime? QuotedDateUtc,
    decimal? AgreedValue,
    DateTime? AgreedDateUtc,
    decimal? ActualValue,
    string Status,
    string? Notes
);