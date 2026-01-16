using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mep1.Erp.TimesheetWeb.Services;
using Mep1.Erp.Core.Contracts;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public sealed class HistoryModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;

    public HistoryModel(ErpTimesheetApiClient api)
    {
        _api = api;
    }

    public List<TimesheetEntrySummaryDto> Entries { get; private set; } = new();

    // Paging (simple + safe)
    [BindProperty(SupportsGet = true)]
    public int Skip { get; set; } = 0;

    [BindProperty(SupportsGet = true)]
    public int Take { get; set; } = 100;

    public int? WorkerId { get; private set; }
    public string? WorkerName { get; private set; }

    public async Task<IActionResult> OnGet()
    {
        if (HttpContext.Session.GetString("MustChangePassword") == "true")
        {
            return RedirectToPage("/Timesheet/Profile");
        }

        WorkerId = HttpContext.Session.GetInt32("WorkerId");
        if (WorkerId is null)
            return RedirectToPage("/Timesheet/Login");

        WorkerName = HttpContext.Session.GetString("WorkerName");

        if (Skip < 0) Skip = 0;
        if (Take <= 0) Take = 100;
        if (Take > 200) Take = 200;

        Entries = await _api.GetTimesheetEntriesAsync(Skip, Take)
                  ?? new List<TimesheetEntrySummaryDto>();

        Weeks = Entries
            .GroupBy(e => GetWeekEndingFriday(e.Date))
            .OrderByDescending(g => g.Key) // most recent week first
            .Select(g => new WeekGroup
            {
                WeekEndingFriday = g.Key,
                Entries = g.OrderByDescending(e => e.Date).ToList()
            })
            .ToList();

        return Page();
    }

    public bool HasPrev => Skip > 0;
    public bool HasNext => Entries.Count == Take;

    public int PrevSkip => Math.Max(0, Skip - Take);
    public int NextSkip => Skip + Take;

    public async Task<IActionResult> OnPostDeleteAsync(int id, int skip = 0, int take = 100)
    {
        if (HttpContext.Session.GetString("MustChangePassword") == "true")
        {
            return RedirectToPage("/Timesheet/Profile");
        }

        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        await _api.DeleteTimesheetEntryAsync(id);

        // return to same page of results
        return RedirectToPage("/Timesheet/History", new { skip, take });
    }

    private static DateTime GetWeekEndingFriday(DateTime date)
    {
        var d = date.Date;
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)d.DayOfWeek + 7) % 7;
        return d.AddDays(daysUntilFriday);
    }

    public sealed class WeekGroup
    {
        public DateTime WeekEndingFriday { get; init; }
        public DateTime WeekStartSaturday => WeekEndingFriday.AddDays(-6);
        public List<TimesheetEntrySummaryDto> Entries { get; init; } = new();
        public decimal TotalHours => Entries.Sum(e => e.Hours);
    }

    public List<WeekGroup> Weeks { get; private set; } = new();

}
