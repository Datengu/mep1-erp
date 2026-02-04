namespace Mep1.Erp.Core.Contracts;

public record ProjectLabourByPersonRowDto(
    string WorkerInitials,
    string WorkerName,
    decimal Hours,
    decimal Cost);

public record ProjectRecentEntryRowDto(
    DateTime Date,
    string WorkerInitials,
    decimal Hours,
    decimal Cost,
    string TaskDescription);

public record ProjectApplicationRowDto(
    string ProjectCode,
    int ApplicationNumber,
    DateTime? ApplicationDate,
    decimal NetAmount,

    // linked invoice (optional)
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    decimal? InvoiceNet,

    // payment lives on invoice (optional)
    decimal? PaymentAmount,
    DateTime? PaidDate
);

public record ProjectInvoiceRowDto(
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    decimal NetAmount,
    decimal OutstandingNet,
    string? Status,
    decimal? PaidAmount,
    DateTime? PaidDate);

public record SupplierCostRowDto(
    int Id,
    DateTime? Date,
    int SupplierId,
    string SupplierName,
    decimal Amount,
    string? Note);

public record ProjectDrilldownDto(
    string JobNameOrNumber,
    string? BaseCode,
    List<ProjectLabourByPersonRowDto> LabourThisMonth,
    List<ProjectLabourByPersonRowDto> LabourAllTime,
    List<ProjectRecentEntryRowDto> RecentEntries,
    List<ProjectApplicationRowDto> Applications,
    List<ProjectInvoiceRowDto> Invoices,
    List<SupplierCostRowDto> SupplierCosts);

