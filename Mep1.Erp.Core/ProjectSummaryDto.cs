namespace Mep1.Erp.Core
{
    public sealed class ProjectSummaryDto
    {
        public string JobNameOrNumber { get; init; } = "";
        public string? BaseCode { get; init; }

        public bool IsActive { get; init; }

        public decimal LabourCost { get; init; }
        public decimal SupplierCost { get; init; }
        public decimal TotalCost { get; init; }

        public decimal InvoicedNet { get; init; }
        public decimal InvoicedGross { get; init; }

        public decimal ProfitNet { get; init; }
        public decimal ProfitGross { get; init; }
    }
}
