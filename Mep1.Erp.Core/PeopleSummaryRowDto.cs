using System;

namespace Mep1.Erp.Core
{
    public sealed class PeopleSummaryRowDto
    {
        public int WorkerId { get; init; }
        public string Initials { get; init; } = "";
        public string Name { get; init; } = "";

        public decimal? CurrentRatePerHour { get; init; }
        public DateTime? LastWorkedDate { get; init; }

        public decimal HoursThisMonth { get; init; }
        public decimal CostThisMonth { get; init; }
        public bool IsActive { get; init; }
    }
}
