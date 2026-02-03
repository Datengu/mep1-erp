namespace Mep1.Erp.Core.Contracts;
public sealed class ApplicationListEntryDto
{
    public int Id { get; set; }
    public string ApplicationNumber { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string JobName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? ApplicationDate { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? AgereedNetAmount { get; set; }
    public DateTime? DateAgreed { get; set; }
    public string? ExternalReference { get; set; }
    public string Notes { get; set; } = "";
    public int? InvoiceId { get; set; }
}