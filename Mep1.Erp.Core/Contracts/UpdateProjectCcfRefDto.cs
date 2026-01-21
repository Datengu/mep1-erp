namespace Mep1.Erp.Core.Contracts;
public record UpdateProjectCcfRefDto(
    decimal? EstimatedValue,
    decimal? QuotedValue,
    DateTime? QuotedDateUtc,
    decimal? AgreedValue,
    DateTime? AgreedDateUtc,
    decimal? ActualValue,
    string Status,
    string? Notes
);