using System;

namespace Mep1.Erp.Core
{
    public sealed class DueScheduleEntryDto
    {
        public DateTime WeekStart { get; init; }
        public DateTime WeekEnd { get; init; }
        public decimal OutstandingNet { get; init; }
        public int InvoiceCount { get; init; }
    }
}