namespace Mep1.Erp.Core
{
    public sealed class PersonDrilldownDto
    {
        public List<WorkerRateDto> Rates { get; init; } = new();
        public List<PersonProjectBreakdownRowDto> Projects { get; init; } = new();
        public List<PersonRecentEntryRowDto> RecentEntries { get; init; } = new();
    }

    public sealed class WorkerRateDto
    {
        public DateTime ValidFrom { get; init; }
        public DateTime? ValidTo { get; init; }
        public decimal RatePerHour { get; init; }
    }

    public sealed class PersonProjectBreakdownRowDto
    {
        public string ProjectLabel { get; init; } = "";
        public string? ProjectCode { get; init; }
        public decimal Hours { get; init; }
        public decimal Cost { get; init; }
    }

    public sealed class PersonRecentEntryRowDto
    {
        public DateTime Date { get; init; }
        public string ProjectLabel { get; init; } = "";
        public string? ProjectCode { get; init; }
        public decimal Hours { get; init; }
        public string TaskDescription { get; init; } = "";
        public decimal Cost { get; init; }
    }
}
