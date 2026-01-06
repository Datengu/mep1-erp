using Mep1.Erp.Core;
using Mep1.Erp.TimesheetWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Mep1.Erp.TimesheetWeb.Pages.Timesheet;

public class EvidencePackModel : PageModel
{
    private readonly ErpTimesheetApiClient _api;
    private readonly TechnicalDiaryPdfBuilder _pdf;

    public EvidencePackModel(ErpTimesheetApiClient api, TechnicalDiaryPdfBuilder pdf)
    {
        _api = api;
        _pdf = pdf;
    }

    public string WorkerName { get; set; } = "";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [DataType(DataType.Date)]
        public DateTime From { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime To { get; set; }
    }

    private static DateTime GetWeekEndingSunday(DateTime date)
    {
        var d = date.Date;
        var daysUntilSunday = (7 - (int)d.DayOfWeek) % 7; // Sunday=0 => 0
        return d.AddDays(daysUntilSunday);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        // signature gate
        var sig = await _api.GetWorkerSignatureAsync(workerId.Value);
        if (sig is null)
            return RedirectToPage("/Timesheet/Login");

        WorkerName = sig.Name;

        if (string.IsNullOrWhiteSpace(sig.SignatureName))
        {
            // send them to signature page then back here
            return RedirectToPage("/Timesheet/Signature", new { returnTo = "/Timesheet/EvidencePack" });
        }

        // sensible default period: last full week ending Sunday
        var today = DateTime.Today;
        var daysSinceSunday = (int)today.DayOfWeek; // Sunday=0
        var lastSunday = today.AddDays(-daysSinceSunday);
        var weekStart = lastSunday.AddDays(-6);

        Input.From = weekStart;
        Input.To = lastSunday;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var workerId = HttpContext.Session.GetInt32("WorkerId");
        if (workerId is null)
            return RedirectToPage("/Timesheet/Login");

        if (!ModelState.IsValid)
            return Page();

        if (Input.To.Date < Input.From.Date)
        {
            ModelState.AddModelError(string.Empty, "To date must be on or after From date.");
            return Page();
        }

        if (Input.To.Date > DateTime.Today)
        {
            ModelState.AddModelError(string.Empty, "Future dates are not allowed.");
            return Page();
        }

        // signature gate again on POST
        var sig = await _api.GetWorkerSignatureAsync(workerId.Value);
        if (sig is null) return RedirectToPage("/Timesheet/Login");
        if (string.IsNullOrWhiteSpace(sig.SignatureName))
            return RedirectToPage("/Timesheet/Signature", new { returnTo = "/Timesheet/EvidencePack" });

        var entries = await FetchEntriesForRangeAsync(workerId.Value, Input.From, Input.To);

        if (entries.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "No timesheet entries found in that date range.");
            WorkerName = sig.Name;
            return Page();
        }

        // group by week ending Sunday
        var grouped = entries
            .GroupBy(e => GetWeekEndingSunday(e.Date))
            .OrderBy(g => g.Key)
            .ToList();

        var initials = sig.Initials.ToUpperInvariant();

        // Create ZIP in-memory
        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var week in grouped)
            {
                var weekEnding = week.Key;
                var weekEntries = week.ToList();

                var pdfBytes = _pdf.BuildWeekPdf(
                    workerName: sig.Name,
                    workerSignatureName: sig.SignatureName!,
                    weekEndingSunday: weekEnding,
                    entries: weekEntries);

                var fileName =
                    $"MEP1 BIM Technical Diary ({initials}){weekEnding:yyyyMMdd}.pdf";

                var entry = archive.CreateEntry(fileName, System.IO.Compression.CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                await entryStream.WriteAsync(pdfBytes, 0, pdfBytes.Length);
            }
        }

        zipStream.Position = 0;

        var zipName =
            $"MEP1 BIM Technical Diary ({initials}) Pack {Input.From:yyyy-MM-dd} to {Input.To:yyyy-MM-dd}.zip";
        return File(zipStream.ToArray(), "application/zip", zipName);
    }

    private async Task<List<TimesheetEntrySummaryDto>> FetchEntriesForRangeAsync(int workerId, DateTime from, DateTime to)
    {
        const int take = 200;
        var skip = 0;

        var results = new List<TimesheetEntrySummaryDto>();

        while (true)
        {
            var page = await _api.GetTimesheetEntriesAsync(workerId, skip, take);

            if (page.Count == 0)
                break;

            foreach (var e in page)
            {
                var d = e.Date.Date;

                if (d > to.Date)
                    continue;

                if (d < from.Date)
                    return results; // early stop due to descending order

                results.Add(e);
            }

            skip += take;
        }

        return results;
    }

}
