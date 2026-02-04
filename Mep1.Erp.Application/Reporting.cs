using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mep1.Erp.Application
{
    public record DashboardSummary(
        decimal OutstandingNet,
        decimal OutstandingGross,
        int UnpaidInvoiceCount,
        int ActiveProjects,
        DateTime? LatestTimesheetDate,
        DateTime? LatestInvoiceDate,
        int OverdueInvoiceCount,
        decimal OverdueOutstandingNet,
        int DueNext30DaysCount,
        decimal DueNext30DaysOutstandingNet,
        DateTime? NextDueDate,
        int UpcomingApplicationCount,
        DateTime? NextApplicationDate);

    public record ProjectSummary(
        string JobNameOrNumber,
        string? BaseCode,
        bool IsActive,
        decimal LabourCost,
        decimal SupplierCost,
        decimal TotalCost,
        decimal InvoicedNet,
        decimal InvoicedGross,
        decimal ProfitNet,
        decimal ProfitGross);

    public record DueScheduleEntry(
        DateTime WeekStart,
        DateTime WeekEnd,
        decimal OutstandingNet,
        int InvoiceCount);

    public record UpcomingApplicationEntry(
        string ProjectCode,
        string ProjectLabel,
        DateTime ApplicationDate,
        int DaysUntil,
        string ScheduleType,
        string? RuleType,
        string? Notes);

    public record ProjectInvoiceRow(
        string InvoiceNumber,
        DateTime InvoiceDate,
        DateTime? DueDate,
        decimal NetAmount,
        decimal OutstandingNet,
        string? Status,
        decimal? PaymentAmount,
        DateTime? PaidDate);

    public record SupplierCostRow(
        int Id,
        DateTime? Date,
        int SupplierId,
        string SupplierName,
        decimal Amount,
        string? Note);

    public record InvoiceListEntry(
        int Id,
        string InvoiceNumber,
        string ProjectCode,
        string? JobName,
        string? ClientName,
        int? ApplicationId,
        string? ApplicationNumber,
        DateTime InvoiceDate,
        DateTime? DueDate,
        decimal NetAmount,
        decimal? GrossAmount,
        decimal? PaymentAmount,
        DateTime? PaidDate,
        string? Status,
        bool IsPaid,
        decimal OutstandingNet,
        decimal OutstandingGross,
        string? FilePath,
        string? Notes);

    public static class Reporting
    {
        // Postgres + Npgsql: avoid sending DateTime.Kind=Unspecified into timestamptz columns/params.
        private static DateTime UtcTodayDate() => DateTime.UtcNow.Date;

        private static DateTime AsUtcDate(DateTime dt)
        {
            // dt.Date preserves Kind, but if dt.Kind was Unspecified it stays Unspecified.
            // Force UTC for all "date-only" boundary values used in DB predicates.
            var d = dt.Date;
            return d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);
        }

        private static DateTime? AsUtcDate(DateTime? dt)
            => dt.HasValue ? AsUtcDate(dt.Value) : null;

        public static List<ProjectSummary> GetProjectCostVsInvoiced(AppDbContext db)
        {
            // 1) Load projects once (no tracking)
            var projects = db.Projects
                .AsNoTracking()
                .Where(p => p.IsRealProject)
                .OrderBy(p => p.JobNameOrNumber)
                .Select(p => new
                {
                    p.Id,
                    p.JobNameOrNumber,
                    p.IsActive
                })
                .ToList();

            if (projects.Count == 0)
                return new List<ProjectSummary>();

            var projectIds = projects.Select(p => p.Id).ToList();

            // 2) Precompute base codes for projects (in-memory)
            var baseCodeByProjectId = projects.ToDictionary(
                p => p.Id,
                p => ProjectCodeHelpers.GetBaseProjectCode(p.JobNameOrNumber));

            // 3) Invoices: load only what we need, then aggregate by trimmed ProjectCode
            // (Trimming inside SQL is awkward/slow; do it once in-memory)
            var invoiceSumsByCode = db.Invoices
                .AsNoTracking()
                .Where(i =>
                    i.ProjectCode != null &&
                    i.ProjectCode != "" &&
                    i.Status != "VOID"
                )
                .Select(i => new { i.ProjectCode, i.NetAmount, i.GrossAmount })
                .ToList()
                .GroupBy(i => (i.ProjectCode ?? string.Empty).Trim())
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Net = g.Sum(x => x.NetAmount),
                        Gross = g.Sum(x => x.GrossAmount ?? 0m)
                    });

            // 4) Supplier costs: SQLite provider can't translate SUM(decimal) reliably.
            // Pull minimal data and aggregate client-side (works on SQLite + Postgres).
            var supplierCostByProjectId = db.SupplierCosts
                .AsNoTracking()
                .Where(sc => projectIds.Contains(sc.ProjectId))
                .Select(sc => new { sc.ProjectId, sc.Amount })
                .ToList()
                .GroupBy(x => x.ProjectId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            // 5) Timesheet entries: load only required fields for ALL projects once
            var entries = db.TimesheetEntries
                .AsNoTracking()
                .Where(e => projectIds.Contains(e.ProjectId))
                .Select(e => new { e.ProjectId, e.WorkerId, e.Date, e.Hours })
                .ToList();

            // If there are no entries at all, we still might include projects with a base code
            // (because they may have invoices).
            // We'll decide inclusion later.

            // 6) Rates: load once for the workerIds that appear in these entries
            var workerIds = entries.Select(e => e.WorkerId).Distinct().ToList();

            var ratesByWorker = workerIds.Count == 0
                ? new Dictionary<int, List<WorkerRate>>()
                : db.WorkerRates
                    .AsNoTracking()
                    .Where(r => workerIds.Contains(r.WorkerId))
                    .OrderByDescending(r => r.ValidFrom)
                    .ToList()
                    .GroupBy(r => r.WorkerId)
                    .ToDictionary(g => g.Key, g => g.ToList());

            decimal GetRateOnInMemory(int workerId, DateTime date)
            {
                if (!ratesByWorker.TryGetValue(workerId, out var rates) || rates.Count == 0)
                    return 0m;

                // rates sorted by ValidFrom DESC
                foreach (var r in rates)
                {
                    if (date >= r.ValidFrom && (r.ValidTo == null || date < r.ValidTo.Value))
                        return r.RatePerHour;
                }

                return 0m;
            }

            // 7) Labour cost per project (in-memory aggregation, no DB calls)
            var labourCostByProjectId = new Dictionary<int, decimal>();
            var hasEntriesByProjectId = new HashSet<int>();

            foreach (var e in entries)
            {
                hasEntriesByProjectId.Add(e.ProjectId);

                var rate = GetRateOnInMemory(e.WorkerId, e.Date);
                var cost = e.Hours * rate;

                if (labourCostByProjectId.TryGetValue(e.ProjectId, out var cur))
                    labourCostByProjectId[e.ProjectId] = cur + cost;
                else
                    labourCostByProjectId[e.ProjectId] = cost;
            }

            // 8) Build final summary list
            var result = new List<ProjectSummary>(projects.Count);

            foreach (var p in projects)
            {
                var baseCode = baseCodeByProjectId[p.Id];
                var hasProjectCode = !string.IsNullOrWhiteSpace(baseCode);

                var hasEntries = hasEntriesByProjectId.Contains(p.Id);

                // Preserve your old behaviour: if there are no entries AND no base code, skip.
                if (!hasEntries && !hasProjectCode)
                    continue;

                var labourCost = labourCostByProjectId.TryGetValue(p.Id, out var lc) ? lc : 0m;
                var supplierCost = supplierCostByProjectId.TryGetValue(p.Id, out var sc) ? sc : 0m;

                decimal invoicedNet = 0m;
                decimal invoicedGross = 0m;

                if (hasProjectCode && baseCode != null && invoiceSumsByCode.TryGetValue(baseCode, out var inv))
                {
                    invoicedNet = inv.Net;
                    invoicedGross = inv.Gross;
                }

                var totalCost = labourCost + supplierCost;

                result.Add(new ProjectSummary(
                    JobNameOrNumber: p.JobNameOrNumber,
                    BaseCode: baseCode,
                    IsActive: p.IsActive,
                    LabourCost: labourCost,
                    SupplierCost: supplierCost,
                    TotalCost: totalCost,
                    InvoicedNet: invoicedNet,
                    InvoicedGross: invoicedGross,
                    ProfitNet: invoicedNet - totalCost,
                    ProfitGross: invoicedGross - totalCost
                ));
            }

            return result;
        }

        public static DashboardSummary GetDashboardSummary(AppDbContext db, AppSettings settings)
        {
            var today = UtcTodayDate();
            var in30Days = today.AddDays(30);

            // Read-only dashboard: no tracking
            var invoices = db.Invoices
                .AsNoTracking()
                .ToList();

            decimal outstandingNet = 0m;
            decimal outstandingGross = 0m;
            int unpaidCount = 0;

            int overdueCount = 0;
            decimal overdueOutstandingNet = 0m;

            int dueNext30DaysCount = 0;
            decimal dueNext30DaysOutstandingNet = 0m;

            DateTime? nextDueDate = null;
            DateTime? latestInvoiceDate = null;

            foreach (var i in invoices)
            {
                // Track latest invoice date (all invoices)
                if (latestInvoiceDate == null || i.InvoiceDate > latestInvoiceDate.Value)
                    latestInvoiceDate = i.InvoiceDate;

                var outNet = i.GetOutstandingNet();
                if (outNet <= 0m)
                    continue; // ignore fully-paid invoices

                // Open invoice
                unpaidCount++;

                var outGross = i.GetOutstandingGross();

                outstandingNet += outNet;
                outstandingGross += outGross;

                if (i.DueDate.HasValue)
                {
                    var due = i.DueDate.Value.Date;

                    if (due < today)
                    {
                        overdueCount++;
                        overdueOutstandingNet += outNet;
                    }
                    else
                    {
                        // Next upcoming due date (non-overdue)
                        if (nextDueDate == null || due < nextDueDate.Value)
                            nextDueDate = due;

                        if (due <= in30Days)
                        {
                            dueNext30DaysCount++;
                            dueNext30DaysOutstandingNet += outNet;
                        }
                    }
                }
            }

            // Active projects = real projects that are marked active
            int activeProjects = db.Projects
                .AsNoTracking()
                .Count(p => p.IsActive && p.IsRealProject);

            DateTime? latestTimesheetDate = db.TimesheetEntries
                .AsNoTracking()
                .OrderByDescending(e => e.Date)
                .Select(e => (DateTime?)e.Date)
                .FirstOrDefault();

            // 🔵 Upcoming applications (next N days)
            var upcomingApps = GetUpcomingApplications(db, daysAhead: settings.UpcomingApplicationsDaysAhead);
            int upcomingAppCount = upcomingApps.Count;

            DateTime? nextAppDate = upcomingApps
                .Select(a => (DateTime?)a.ApplicationDate)
                .OrderBy(d => d)
                .FirstOrDefault();

            return new DashboardSummary(
                OutstandingNet: outstandingNet,
                OutstandingGross: outstandingGross,
                UnpaidInvoiceCount: unpaidCount,
                ActiveProjects: activeProjects,
                LatestTimesheetDate: latestTimesheetDate,
                LatestInvoiceDate: latestInvoiceDate,
                OverdueInvoiceCount: overdueCount,
                OverdueOutstandingNet: overdueOutstandingNet,
                DueNext30DaysCount: dueNext30DaysCount,
                DueNext30DaysOutstandingNet: dueNext30DaysOutstandingNet,
                NextDueDate: nextDueDate,
                UpcomingApplicationCount: upcomingAppCount,
                NextApplicationDate: nextAppDate);
        }

        public record PeopleSummaryRow(
            int WorkerId,
            string Initials,
            string Name,
            decimal? CurrentRatePerHour,
            DateTime? LastWorkedDate,
            decimal HoursThisMonth,
            decimal CostThisMonth,
            bool IsActive);

        public static List<PeopleSummaryRow> GetPeopleSummary(AppDbContext db, DateTime? todayOverride = null)
        {
            var today = AsUtcDate(todayOverride ?? UtcTodayDate());
            var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Workers (minimal fields, no tracking)
            var workers = db.Workers
                .AsNoTracking()
                .Select(w => new { w.Id, w.Initials, w.Name, w.IsActive })
                .ToList();

            if (workers.Count == 0)
                return new List<PeopleSummaryRow>();

            var workerIds = workers.Select(w => w.Id).ToList();

            // Last worked date per worker (all time) - do in SQL (fast, safe)
            var lastWorkedByWorkerId = db.TimesheetEntries
                .AsNoTracking()
                .Where(e => workerIds.Contains(e.WorkerId))
                .GroupBy(e => e.WorkerId)
                .Select(g => new { WorkerId = g.Key, LastWorked = (DateTime?)g.Max(x => x.Date) })
                .ToList()
                .ToDictionary(x => x.WorkerId, x => x.LastWorked);

            // Month entries: only pull the fields we need, for only the month range
            // Avoid e.Date.Date in the predicate; it kills index usage and can translate poorly.
            var monthEntries = db.TimesheetEntries
                .AsNoTracking()
                .Where(e => workerIds.Contains(e.WorkerId) &&
                            e.Date >= monthStart &&
                            e.Date <= today)
                .Select(e => new { e.WorkerId, e.Date, e.Hours })
                .ToList();

            var hoursThisMonthByWorkerId = monthEntries
                .GroupBy(e => e.WorkerId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));

            // Rates: load once for only relevant workers, pre-group by worker
            var ratesByWorker = db.WorkerRates
                .AsNoTracking()
                .Where(r => workerIds.Contains(r.WorkerId))
                .OrderByDescending(r => r.ValidFrom)
                .ToList()
                .GroupBy(r => r.WorkerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            decimal GetRateOnInMemory(int workerId, DateTime workDate)
            {
                if (!ratesByWorker.TryGetValue(workerId, out var rates) || rates.Count == 0)
                    return 0m;

                // rates are sorted by ValidFrom DESC
                foreach (var r in rates)
                {
                    if (workDate >= r.ValidFrom && (r.ValidTo == null || workDate < r.ValidTo.Value))
                        return r.RatePerHour;
                }

                return 0m;
            }

            // Cost this month: sum(entry hours * rate on that date) (in-memory, no N+1)
            var costThisMonthByWorkerId = new Dictionary<int, decimal>();
            foreach (var e in monthEntries)
            {
                var rate = GetRateOnInMemory(e.WorkerId, e.Date);
                var cost = e.Hours * rate;

                if (costThisMonthByWorkerId.TryGetValue(e.WorkerId, out var cur))
                    costThisMonthByWorkerId[e.WorkerId] = cur + cost;
                else
                    costThisMonthByWorkerId[e.WorkerId] = cost;
            }

            decimal? GetCurrentRate(int workerId)
            {
                if (!ratesByWorker.TryGetValue(workerId, out var rates) || rates.Count == 0)
                    return null;

                // “current” means valid on 'today'
                foreach (var r in rates)
                {
                    if (today >= r.ValidFrom && (r.ValidTo == null || today < r.ValidTo.Value))
                        return r.RatePerHour;
                }

                return null;
            }

            return workers
                .OrderBy(w => w.Name)
                .Select(w => new PeopleSummaryRow(
                    WorkerId: w.Id,
                    Initials: w.Initials,
                    Name: w.Name,
                    CurrentRatePerHour: GetCurrentRate(w.Id),
                    LastWorkedDate: lastWorkedByWorkerId.TryGetValue(w.Id, out var last) ? last : null,
                    HoursThisMonth: hoursThisMonthByWorkerId.TryGetValue(w.Id, out var hrs) ? hrs : 0m,
                    CostThisMonth: costThisMonthByWorkerId.TryGetValue(w.Id, out var cost) ? cost : 0m,
                    IsActive: w.IsActive
                ))
                .ToList();
        }

        public record PersonProjectBreakdownRow(
            string ProjectLabel,
            string? ProjectCode,
            decimal Hours,
            decimal Cost);

        public record PersonRecentEntryRow(
            DateTime Date,
            string ProjectLabel,
            string? ProjectCode,
            decimal Hours,
            string TaskDescription,
            decimal Cost);

        public static List<WorkerRate> GetWorkerRateHistory(AppDbContext db, int workerId)
        {
            return db.WorkerRates
                .Where(r => r.WorkerId == workerId)
                .OrderByDescending(r => r.ValidFrom)
                .ToList();
        }

        public static List<PersonProjectBreakdownRow> GetPersonProjectBreakdown(
            AppDbContext db,
            int workerId,
            DateTime fromInclusive,
            DateTime toInclusive)
        {
            // Pull entries for worker and range
            var entries = db.TimesheetEntries
                .Where(e => e.WorkerId == workerId &&
                            e.Date.Date >= fromInclusive.Date &&
                            e.Date.Date <= toInclusive.Date)
                .ToList();

            if (!entries.Any())
                return new List<PersonProjectBreakdownRow>();

            // Cache projects for labels/codes
            var projectsById = db.Projects.ToDictionary(p => p.Id);

            decimal GetRateOn(DateTime date)
            {
                var rate = db.WorkerRates
                    .Where(r => r.WorkerId == workerId &&
                                date >= r.ValidFrom &&
                                (r.ValidTo == null || date < r.ValidTo))
                    .OrderByDescending(r => r.ValidFrom)
                    .FirstOrDefault();

                return rate?.RatePerHour ?? 0m;
            }

            return entries
                .GroupBy(e => e.ProjectId)
                .Select(g =>
                {
                    var proj = projectsById.TryGetValue(g.Key, out var p) ? p : null;
                    var label = proj?.JobNameOrNumber ?? $"Id {g.Key}";
                    var code = proj != null ? ProjectCodeHelpers.GetBaseProjectCode(proj.JobNameOrNumber) : null;

                    var hours = g.Sum(x => x.Hours);
                    var cost = g.Sum(x => x.Hours * GetRateOn(x.Date));

                    return new PersonProjectBreakdownRow(
                        ProjectLabel: label,
                        ProjectCode: code,
                        Hours: hours,
                        Cost: cost);
                })
                .OrderByDescending(x => x.Hours)
                .ToList();
        }

        public static List<PersonRecentEntryRow> GetPersonRecentEntries(AppDbContext db, int workerId, int take = 20)
        {
            var projectsById = db.Projects.ToDictionary(p => p.Id);

            decimal GetRateOn(DateTime date)
            {
                var rate = db.WorkerRates
                    .Where(r => r.WorkerId == workerId &&
                                date >= r.ValidFrom &&
                                (r.ValidTo == null || date < r.ValidTo))
                    .OrderByDescending(r => r.ValidFrom)
                    .FirstOrDefault();

                return rate?.RatePerHour ?? 0m;
            }

            var entries = db.TimesheetEntries
                .Where(e => e.WorkerId == workerId)
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Take(take)
                .ToList();

            return entries.Select(e =>
            {
                var proj = projectsById.TryGetValue(e.ProjectId, out var p) ? p : null;
                var label = proj?.JobNameOrNumber ?? $"Id {e.ProjectId}";
                var code = proj != null ? ProjectCodeHelpers.GetBaseProjectCode(proj.JobNameOrNumber) : null;

                var cost = e.Hours * GetRateOn(e.Date);

                return new PersonRecentEntryRow(
                    Date: e.Date.Date,
                    ProjectLabel: label,
                    ProjectCode: code,
                    Hours: e.Hours,
                    TaskDescription: e.TaskDescription ?? string.Empty,
                    Cost: cost);
            }).ToList();
        }

        public record ProjectLabourByPersonRow(
            string WorkerInitials,
            string WorkerName,
            decimal Hours,
            decimal Cost);

        public record ProjectRecentEntryRow(
            DateTime Date,
            string WorkerInitials,
            decimal Hours,
            decimal Cost,
            string TaskDescription);

        public static List<ProjectLabourByPersonRow> GetProjectLabourByPerson(
            AppDbContext db,
            int projectId,
            DateTime? fromInclusive = null,
            DateTime? toInclusive = null)
        {
            // Make date filtering index-friendly (avoid e.Date.Date in SQL)
            DateTime? from = fromInclusive?.Date;
            DateTime? toExclusive = toInclusive?.Date.AddDays(1);

            var entriesQuery = db.TimesheetEntries
                .AsNoTracking()
                .Where(e => e.ProjectId == projectId);

            if (from.HasValue)
                entriesQuery = entriesQuery.Where(e => e.Date >= from.Value);

            if (toExclusive.HasValue)
                entriesQuery = entriesQuery.Where(e => e.Date < toExclusive.Value);

            // Pull only what we need into memory (fast, avoids tracking)
            var entries = entriesQuery
                .Select(e => new { e.WorkerId, e.Date, e.Hours })
                .ToList();

            if (entries.Count == 0)
                return new List<ProjectLabourByPersonRow>();

            // Worker ids used by this project/date range
            var workerIds = entries.Select(e => e.WorkerId).Distinct().ToList();

            // Load workers once
            var workersById = db.Workers
                .AsNoTracking()
                .Where(w => workerIds.Contains(w.Id))
                .ToDictionary(w => w.Id);

            // Load rates once (kills N+1 DB queries)
            var ratesByWorker = db.WorkerRates
                .AsNoTracking()
                .Where(r => workerIds.Contains(r.WorkerId))
                .OrderByDescending(r => r.ValidFrom)
                .ToList()
                .GroupBy(r => r.WorkerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            decimal GetRateOnInMemory(int workerId, DateTime date)
            {
                if (!ratesByWorker.TryGetValue(workerId, out var rates) || rates.Count == 0)
                    return 0m;

                // rates are sorted by ValidFrom DESC
                foreach (var r in rates)
                {
                    if (date >= r.ValidFrom && (r.ValidTo == null || date < r.ValidTo.Value))
                        return r.RatePerHour;
                }

                return 0m;
            }

            return entries
                .GroupBy(e => e.WorkerId)
                .Select(g =>
                {
                    Worker? worker;
                    workersById.TryGetValue(g.Key, out worker);

                    var initials = worker?.Initials ?? "";
                    var name = worker?.Name ?? $"WorkerId {g.Key}";

                    var hours = g.Sum(x => x.Hours);
                    var cost = g.Sum(x => x.Hours * GetRateOnInMemory(x.WorkerId, x.Date));

                    return new ProjectLabourByPersonRow(
                        WorkerInitials: initials,
                        WorkerName: name,
                        Hours: hours,
                        Cost: cost);
                })
                .OrderByDescending(x => x.Hours)
                .ToList();
        }

        public static List<ProjectRecentEntryRow> GetProjectRecentEntries(AppDbContext db, int projectId, int take = 25)
        {
            // Pull only required fields (no tracking)
            var entries = db.TimesheetEntries
                .AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Take(take)
                .Select(e => new { e.Date, e.WorkerId, e.Hours, e.TaskDescription })
                .ToList();

            if (entries.Count == 0)
                return new List<ProjectRecentEntryRow>();

            var workerIds = entries.Select(e => e.WorkerId).Distinct().ToList();

            // Load workers once
            var workersById = db.Workers
                .AsNoTracking()
                .Where(w => workerIds.Contains(w.Id))
                .ToDictionary(w => w.Id);

            // Load rates once (kills N+1 DB queries)
            var ratesByWorker = db.WorkerRates
                .AsNoTracking()
                .Where(r => workerIds.Contains(r.WorkerId))
                .OrderByDescending(r => r.ValidFrom)
                .ToList()
                .GroupBy(r => r.WorkerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            decimal GetRateOnInMemory(int workerId, DateTime date)
            {
                if (!ratesByWorker.TryGetValue(workerId, out var rates) || rates.Count == 0)
                    return 0m;

                foreach (var r in rates)
                {
                    if (date >= r.ValidFrom && (r.ValidTo == null || date < r.ValidTo.Value))
                        return r.RatePerHour;
                }

                return 0m;
            }

            return entries.Select(e =>
            {
                Worker? worker;
                workersById.TryGetValue(e.WorkerId, out worker);

                var initials = worker?.Initials ?? "";

                return new ProjectRecentEntryRow(
                    Date: e.Date.Date,
                    WorkerInitials: initials,
                    Hours: e.Hours,
                    Cost: e.Hours * GetRateOnInMemory(e.WorkerId, e.Date),
                    TaskDescription: e.TaskDescription ?? string.Empty);
            }).ToList();
        }

        public static List<Invoice> GetInvoicesForProjectCode(AppDbContext db, string? projectCode)
        {
            if (string.IsNullOrWhiteSpace(projectCode))
                return new List<Invoice>();

            var code = projectCode.Trim();

            return db.Invoices
                .Where(i => i.ProjectCode != null && i.ProjectCode.Trim() == code)
                .OrderByDescending(i => i.InvoiceDate)
                .ThenByDescending(i => i.Id)
                .ToList();
        }

#if DEBUG
        public static List<(string InvoiceNumber, string Status, decimal Net, decimal? Paid, decimal Outstanding)>GetOpenInvoicesDebug(AppDbContext db)
        {
            var invoices = db.Invoices.ToList();

            var openInvoices = invoices
                .Select(i => new
                {
                    i.InvoiceNumber,
                    Status = i.Status ?? string.Empty,
                    Net = i.NetAmount,
                    Paid = i.PaymentAmount,
                    Outstanding = i.GetOutstandingNet()
                })
                .Where(x => x.Outstanding > 0m)
                .ToList();

            return openInvoices
                .Select(x => (x.InvoiceNumber, x.Status, x.Net, x.Paid, x.Outstanding))
                .ToList();
        }
#endif

        public static List<DueScheduleEntry> GetDueSchedule(AppDbContext db)
        {
            var invoices = db.Invoices.ToList();
            var today = UtcTodayDate();

            // Only invoices we still expect money from AND with a due date in the future or today
            var upcoming = invoices
                .Where(i => i.GetOutstandingNet() > 0m && i.DueDate.HasValue)
                .Where(i => i.DueDate!.Value.Date >= today)
                .ToList();

            if (!upcoming.Any())
                return new List<DueScheduleEntry>();

            var grouped = upcoming
                .GroupBy(i => DateHelpers.GetWeekStartMonday(i.DueDate!.Value.Date))
                .OrderBy(g => g.Key)
                // you can adjust how many weeks to show; 8 is a sensible default
                .Take(8)
                .Select(g =>
                {
                    var weekStart = g.Key;
                    var weekEnd = weekStart.AddDays(6);

                    decimal totalOutstanding = g.Sum(i => i.GetOutstandingNet());
                    int count = g.Count();

                    return new DueScheduleEntry(
                        WeekStart: weekStart,
                        WeekEnd: weekEnd,
                        OutstandingNet: totalOutstanding,
                        InvoiceCount: count);
                })
                .ToList();

            return grouped;
        }

        public static List<UpcomingApplicationEntry> GetUpcomingApplications(AppDbContext db, int daysAhead = 30)
        {
            var today = UtcTodayDate();
            var horizon = today.AddDays(daysAhead);
            var labourPeriodStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Load projects once (no tracking, minimal fields)
            var projects = db.Projects
                .AsNoTracking()
                .Where(p => p.IsRealProject)
                .Select(p => new { p.Id, p.JobNameOrNumber })
                .ToList();

            // ProjectId -> BaseCode
            var baseCodeByProjectId = new Dictionary<int, string>(projects.Count);

            // BaseCode -> Label (first encountered JobNameOrNumber for that base code)
            var labelByBaseCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in projects)
            {
                var baseCode = ProjectCodeHelpers.GetBaseProjectCode(p.JobNameOrNumber)?.Trim();
                if (string.IsNullOrWhiteSpace(baseCode))
                    continue;

                baseCodeByProjectId[p.Id] = baseCode;

                if (!labelByBaseCode.ContainsKey(baseCode))
                    labelByBaseCode[baseCode] = p.JobNameOrNumber;
            }

            // Precompute which base codes have had labour since labourPeriodStart
            var recentLabourCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var recentLabourProjectIds = db.TimesheetEntries
                .AsNoTracking()
                .Where(e => e.Date >= labourPeriodStart)
                .Select(e => e.ProjectId)
                .Distinct()
                .ToList();

            foreach (var pid in recentLabourProjectIds)
            {
                if (baseCodeByProjectId.TryGetValue(pid, out var code) && !string.IsNullOrWhiteSpace(code))
                    recentLabourCodes.Add(code);
            }

            bool HasRecentLabourForProjectCode(string code)
                => !string.IsNullOrWhiteSpace(code) && recentLabourCodes.Contains(code.Trim());

            string GetProjectLabel(string code)
            {
                code = (code ?? string.Empty).Trim();
                if (labelByBaseCode.TryGetValue(code, out var label))
                    return label;

                return code;
            }

            // Load schedules once (no tracking)
            var schedules = db.ApplicationSchedules
                .AsNoTracking()
                .Select(s => new
                {
                    s.ProjectCode,
                    s.ScheduleType,
                    s.ApplicationSubmissionDate,
                    s.RuleType,
                    s.RuleValue,
                    s.Notes
                })
                .ToList();

            var result = new List<UpcomingApplicationEntry>();

            // 🔵 1) Fixed rows: literal ApplicationSubmissionDate
            foreach (var sched in schedules.Where(s =>
                         !string.IsNullOrWhiteSpace(s.ScheduleType) &&
                         s.ScheduleType.Equals("Fixed", StringComparison.OrdinalIgnoreCase) &&
                         s.ApplicationSubmissionDate.HasValue))
            {
                var code = (sched.ProjectCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var date = sched.ApplicationSubmissionDate!.Value.Date;
                if (date < today || date > horizon)
                    continue;

                if (!HasRecentLabourForProjectCode(code))
                    continue;

                result.Add(new UpcomingApplicationEntry(
                    ProjectCode: code,
                    ProjectLabel: GetProjectLabel(code),
                    ApplicationDate: date,
                    DaysUntil: (date - today).Days,
                    ScheduleType: sched.ScheduleType!,
                    RuleType: null,
                    Notes: sched.Notes));
            }

            // 🔵 2) Rule rows: expand into dates within [today, horizon]
            foreach (var sched in schedules.Where(s =>
                         !string.IsNullOrWhiteSpace(s.ScheduleType) &&
                         s.ScheduleType.Equals("Rule", StringComparison.OrdinalIgnoreCase)))
            {
                var code = (sched.ProjectCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var ruleType = sched.RuleType?.Trim();
                if (string.IsNullOrEmpty(ruleType))
                    continue;

                // For OnCompletion or Unknown we don't pre-generate dates
                if (ruleType.Equals("OnCompletion", StringComparison.OrdinalIgnoreCase) ||
                    ruleType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!HasRecentLabourForProjectCode(code))
                    continue;

                var label = GetProjectLabel(code);

                if (ruleType.Equals("EndOfMonth", StringComparison.OrdinalIgnoreCase))
                {
                    var cursor = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    while (cursor <= horizon)
                    {
                        var lastDay = new DateTime(
                            cursor.Year,
                            cursor.Month,
                            DateTime.DaysInMonth(cursor.Year, cursor.Month),
                            0, 0, 0,
                            DateTimeKind.Utc);

                        if (lastDay >= today && lastDay <= horizon)
                        {
                            result.Add(new UpcomingApplicationEntry(
                                ProjectCode: code,
                                ProjectLabel: label,
                                ApplicationDate: lastDay.Date,
                                DaysUntil: (lastDay.Date - today).Days,
                                ScheduleType: sched.ScheduleType!,
                                RuleType: sched.RuleType,
                                Notes: sched.Notes));
                        }

                        cursor = cursor.AddMonths(1);
                    }
                }
                else if (ruleType.Equals("SpecificDay", StringComparison.OrdinalIgnoreCase) && sched.RuleValue.HasValue)
                {
                    int day = sched.RuleValue.Value;
                    if (day < 1) day = 1;

                    var cursor = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    while (cursor <= horizon)
                    {
                        int dim = DateTime.DaysInMonth(cursor.Year, cursor.Month);
                        int safeDay = Math.Min(day, dim);

                        var d = new DateTime(cursor.Year, cursor.Month, safeDay, 0, 0, 0, DateTimeKind.Utc);

                        if (d >= today && d <= horizon)
                        {
                            result.Add(new UpcomingApplicationEntry(
                                ProjectCode: code,
                                ProjectLabel: label,
                                ApplicationDate: d.Date,
                                DaysUntil: (d.Date - today).Days,
                                ScheduleType: sched.ScheduleType!,
                                RuleType: sched.RuleType,
                                Notes: sched.Notes));
                        }

                        cursor = cursor.AddMonths(1);
                    }
                }
            }

            // Remove duplicates & sort
            return result
                .GroupBy(r => new { r.ProjectCode, r.ApplicationDate })
                .Select(g => g.First())
                .OrderBy(r => r.ApplicationDate)
                .ThenBy(r => r.ProjectCode)
                .ToList();
        }

        public static List<ProjectInvoiceRow> GetProjectInvoiceRows(AppDbContext db, string? projectCode)
        {
            var invoices = GetInvoicesForProjectCode(db, projectCode);

            return invoices.Select(i => new ProjectInvoiceRow(
                InvoiceNumber: i.InvoiceNumber,
                InvoiceDate: i.InvoiceDate,
                DueDate: i.DueDate,
                NetAmount: i.NetAmount,
                OutstandingNet: i.GetOutstandingNet(),
                Status: i.Status,
                PaymentAmount: i.PaymentAmount,
                PaidDate: i.PaidDate
            )).ToList();
        }

        public static List<InvoiceListEntry> GetInvoiceList(AppDbContext db)
        {
            // Keep all business rules server-side (void/cancel/credit handling etc.)
            // Uses your existing InvoiceHelpers extension methods.
            var invoices = db.Invoices
                .OrderByDescending(i => i.InvoiceDate)
                .ThenByDescending(i => i.Id)
                .ToList();

            // Pull linked application numbers in one go (avoid N+1)
            var appIds = invoices
                .Where(i => i.ApplicationId.HasValue)
                .Select(i => i.ApplicationId!.Value)
                .Distinct()
                .ToList();

            var appNoById = appIds.Count == 0
                ? new Dictionary<int, int>()
                : db.Applications
                    .Where(a => appIds.Contains(a.Id))
                    .Select(a => new { a.Id, a.ApplicationNumber })
                    .ToDictionary(x => x.Id, x => x.ApplicationNumber);

            static string? FormatAppRef(int? appNo)
            {
                if (!appNo.HasValue) return null;
                return "APP" + appNo.Value.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            }

            return invoices.Select(i =>
            {
                int? appNo = null;
                if (i.ApplicationId.HasValue && appNoById.TryGetValue(i.ApplicationId.Value, out var n))
                    appNo = n;

                return new InvoiceListEntry(
                    Id: i.Id,
                    InvoiceNumber: i.InvoiceNumber,
                    ProjectCode: i.ProjectCode,
                    JobName: i.JobName,
                    ClientName: i.ClientName,

                    ApplicationId: i.ApplicationId,
                    ApplicationNumber: FormatAppRef(appNo),

                    InvoiceDate: i.InvoiceDate,
                    DueDate: i.DueDate,
                    NetAmount: i.NetAmount,
                    GrossAmount: i.GrossAmount,
                    PaymentAmount: i.PaymentAmount,
                    PaidDate: i.PaidDate,
                    Status: i.Status,
                    IsPaid: i.IsPaid,
                    OutstandingNet: i.GetOutstandingNet(),
                    OutstandingGross: i.GetOutstandingGross(),
                    FilePath: i.FilePath,
                    Notes: i.Notes
                );
            }).ToList();
        }

        public static List<SupplierCostRow> GetProjectSupplierCostRows(AppDbContext db, int projectId)
        {
            return db.SupplierCosts
                .Where(sc => sc.ProjectId == projectId)
                .OrderByDescending(sc => sc.Date.HasValue)
                .ThenByDescending(sc => sc.Date)
                .ThenByDescending(sc => sc.Id)
                .Select(sc => new SupplierCostRow(
                    sc.Id,
                    sc.Date,
                    sc.SupplierId,
                    sc.Supplier.Name,
                    sc.Amount,
                    sc.Note))
                .ToList();
        }

        public record ProjectIncomingRow(
            decimal? ApplicationNet,
            DateTime? ApplicationDate,
            string? InvoiceNumber,
            decimal? InvoiceNet,
            DateTime? InvoiceDate,
            decimal? PaymentValue,
            DateTime? PaymentDate
        );

    }
}
