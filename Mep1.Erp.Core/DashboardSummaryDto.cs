using System;

namespace Mep1.Erp.Core
{
    public sealed class DashboardSummaryDto
    {
        public decimal OutstandingNet { get; init; }
        public decimal OutstandingGross { get; init; }
        public int UnpaidInvoiceCount { get; init; }
        public int ActiveProjects { get; init; }
        public DateTime? LatestTimesheetDate { get; init; }
        public DateTime? LatestInvoiceDate { get; init; }
        public int OverdueInvoiceCount { get; init; }
        public decimal OverdueOutstandingNet { get; init; }
        public int DueNext30DaysCount { get; init; }
        public decimal DueNext30DaysOutstandingNet { get; init; }
        public DateTime? NextDueDate { get; init; }
        public int UpcomingApplicationCount { get; init; }
        public DateTime? NextApplicationDate { get; init; }
    }
}
