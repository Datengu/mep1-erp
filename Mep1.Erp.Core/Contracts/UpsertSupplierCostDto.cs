namespace Mep1.Erp.Core.Contracts;

public record UpsertSupplierCostDto(
    int SupplierId,
    DateTime? Date,
    decimal Amount,
    string? Note);
