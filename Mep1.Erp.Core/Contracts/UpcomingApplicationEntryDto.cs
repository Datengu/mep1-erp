using System;

namespace Mep1.Erp.Core.Contracts
{
    public sealed class UpcomingApplicationEntryDto
    {
        public string ProjectCode { get; init; } = "";
        public string ProjectLabel { get; init; } = "";
        public DateTime ApplicationDate { get; init; }
        public int DaysUntil { get; init; }
        public string ScheduleType { get; init; } = "";
        public string? RuleType { get; init; }
        public string? Notes { get; init; }
    }
}