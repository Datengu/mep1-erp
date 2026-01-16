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
        if (User.FindFirst("must_change_password")?.Value == "true")
            return RedirectToPage("/Timesheet/Profile");

        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Timesheet/Login");

        WorkerId = int.Parse(User.FindFirst("wid")?.Value ?? "0");
        if (WorkerId <= 0)
            return RedirectToPage("/Timesheet/Login");

        // optional UI name (only if you want it and only if you set it in Login)
        WorkerName = User.FindFirst("display_name")?.Value;

        if (Skip < 0) Skip = 0;
        if (Take <= 0) Take = 100;
        if (Take > 200) Take = 200;

        Entries = await _api.GetTimesheetEntriesAsync(Skip, Take)
                  ?? new List<TimesheetEntrySummaryDto>();

        Weeks = Entries
            .GroupBy(e => GetWeekEndingFriday(e.Date))
            .OrderByDescending(g => g.Key)
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
        if (User.FindFirst("must_change_password")?.Value == "true")
            return RedirectToPage("/Timesheet/Profile");

        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Timesheet/Login");

        var workerId = int.Parse(User.FindFirst("wid")?.Value ?? "0");

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
