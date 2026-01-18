using Mep1.Erp.Core.Contracts;

namespace Mep1.Erp.Api.Timesheet;
public static class TimesheetCodeProvider
{
    public static readonly IReadOnlyList<TimesheetCodeDto> Codes =
    [
        new("P", "Programmed Drawing Input"),
        new("IC", "Updating to Internal Comments"),
        new("EC", "Updating to External Comments"),
        new("GM", "General Management"),
        new("M", "Meetings"),
        new("RD", "Record Drawings"),
        new("S", "Surveys"),
        new("T", "Training"),
        new("BIM", "BIM Works"),
        new("DC", "Document Control"),
        new("FP", "Fee Proposal"),
        new("BU", "Business Works"),
        new("QA", "Drawing QA Check"),
        new("TP", "Tender Presentation"),
        new("VO", "Variations"),
        new("SI", "Sick"),
        new("HOL", "Holiday"),
    ];
}