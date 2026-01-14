namespace Mep1.Erp.Core.Contracts
{
    public sealed class CreateInvoiceResponseDto
    {
        public int Id { get; set; }

        public string InvoiceNumber { get; set; } = "";

        public int ProjectId { get; set; }
        public string ProjectCode { get; set; } = "";
        public string JobNameOrNumber { get; set; } = "";
        public string CompanyName { get; set; } = "";

        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }

        public decimal NetAmount { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrossAmount { get; set; }

        public string Status { get; set; } = "Unpaid";
        public string? Notes { get; set; }
    }
}
