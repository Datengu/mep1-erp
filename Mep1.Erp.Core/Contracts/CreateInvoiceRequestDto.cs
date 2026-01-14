namespace Mep1.Erp.Core.Contracts
{
    public sealed class CreateInvoiceRequestDto
    {
        public int ProjectId { get; set; }

        public string InvoiceNumber { get; set; } = "";

        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }

        public decimal NetAmount { get; set; }
        public decimal VatRate { get; set; } // e.g. 0.20m for 20%

        public string Status { get; set; } = "Unpaid";

        public string? Notes { get; set; }
    }
}
