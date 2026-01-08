using System;

namespace Mep1.Erp.Core.Contracts
{
    public sealed class InvoiceListEntryDto
    {
        public int Id { get; init; }

        public string InvoiceNumber { get; init; } = "";
        public string ProjectCode { get; init; } = "";
        public string? JobName { get; init; }
        public string? ClientName { get; init; }

        public DateTime InvoiceDate { get; init; }
        public DateTime? DueDate { get; init; }

        public decimal NetAmount { get; init; }
        public decimal? GrossAmount { get; init; }
        public decimal? PaymentAmount { get; init; }
        public DateTime? PaidDate { get; init; }

        public string? Status { get; init; }
        public bool IsPaid { get; init; }

        // Precomputed so Desktop doesn't need InvoiceHelpers / DbContext / business rules
        public decimal OutstandingNet { get; init; }
        public decimal OutstandingGross { get; init; }

        public string? FilePath { get; init; }
        public string? Notes { get; init; }
    }
}
