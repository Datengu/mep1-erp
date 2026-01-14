using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;
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
        string? Status);

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
        public static List<ProjectSummary> GetProjectCostVsInvoiced(AppDbContext db)
        {
            // Cache invoices grouped by ProjectCode (e.g. "PN0051")
            var invoicesByCode = db.Invoices
                .Where(i => !string.IsNullOrWhiteSpace(i.ProjectCode))
                .AsEnumerable()
                .GroupBy(i => i.ProjectCode!.Trim())
                .ToDictionary(g => g.Key, g => g.ToList());

            var projects = db.Projects
                .Where(p => p.IsRealProject)
                .OrderBy(p => p.JobNameOrNumber)
                .ToList();

            var result = new List<ProjectSummary>();

            foreach (var project in projects)
            {
                var baseCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
                var hasProjectCode = !string.IsNullOrEmpty(baseCode);

                var entries = db.TimesheetEntries
                    .Where(e => e.ProjectId == project.Id)
                    .ToList();

                if (!entries.Any() && !hasProjectCode)
                    continue;

                decimal totalLabourCost = 0m;

                foreach (var entry in entries)
                {
                    var ratePerHour = GetRateForWorkerOnDate(db, entry.WorkerId, entry.Date);
                    if (ratePerHour.HasValue)
                    {
                        totalLabourCost += entry.Hours * ratePerHour.Value;
                    }
                }

                var invoicesForProject =
                    hasProjectCode && invoicesByCode.TryGetValue(baseCode!, out var list)
                        ? list
                        : new List<Invoice>();

                decimal totalInvoicedNet = invoicesForProject.Sum(i => i.NetAmount);
                decimal totalInvoicedGross = invoicesForProject.Sum(i => i.GrossAmount ?? 0m);

                var totalSupplierCost = db.SupplierCosts
                    .Where(sc => sc.ProjectId == project.Id)
                    .Select(sc => sc.Amount)
                    .AsEnumerable()
                    .Sum();

                var totalCost = totalLabourCost + totalSupplierCost;

                decimal profitNet = totalInvoicedNet - totalCost;
                decimal profitGross = totalInvoicedGross - totalCost;

                result.Add(new ProjectSummary(
                    project.JobNameOrNumber,
                    baseCode,
                    project.IsActive,
                    totalLabourCost,
                    totalSupplierCost,
                    totalCost,
                    totalInvoicedNet,
                    totalInvoicedGross,
                    profitNet,
                    profitGross));
            }

            return result;
        }

        public static DashboardSummary GetDashboardSummary(AppDbContext db, AppSettings settings)
        {
            var invoices = db.Invoices.ToList();

            // Consider only invoices that still have something outstanding
            var openInvoices = invoices
                .Where(i => i.GetOutstandingNet() > 0m)
                .ToList();

            decimal outstandingNet = openInvoices.Sum(i => i.GetOutstandingNet());
            decimal outstandingGross = openInvoices.Sum(i => i.GetOutstandingGross());

            int unpaidCount = openInvoices.Count;

            // --- NEW: cashflow buckets based on DueDate ---
            var today = DateTime.Today;
            var in30Days = today.AddDays(30);

            var overdue = openInvoices
                .Where(i => i.DueDate.HasValue && i.DueDate.Value.Date < today)
                .ToList();

            var dueNext30Days = openInvoices
                .Where(i => i.DueDate.HasValue &&
                            i.DueDate.Value.Date >= today &&
                            i.DueDate.Value.Date <= in30Days)
                .ToList();

            int overdueCount = overdue.Count;
            decimal overdueOutstandingNet = overdue.Sum(i => i.GetOutstandingNet());

            int dueNext30DaysCount = dueNext30Days.Count;
            decimal dueNext30DaysOutstandingNet = dueNext30Days.Sum(i => i.GetOutstandingNet());

            // Next expected due date among open invoices that are not overdue
            DateTime? nextDueDate = openInvoices
                .Where(i => i.DueDate.HasValue && i.DueDate.Value.Date >= today)
                .Select(i => (DateTime?)i.DueDate.Value.Date)
                .OrderBy(d => d)
                .FirstOrDefault();

            // Active projects = projects that actually have timesheet entries
            int activeProjects = db.TimesheetEntries
                .Select(e => e.ProjectId)
                .Distinct()
                .Count();

            DateTime? latestTimesheetDate = db.TimesheetEntries
                .OrderByDescending(e => e.Date)
                .Select(e => (DateTime?)e.Date)
                .FirstOrDefault();

            DateTime? latestInvoiceDate = invoices
                .OrderByDescending(i => i.InvoiceDate)
                .Select(i => (DateTime?)i.InvoiceDate)
                .FirstOrDefault();

            // 🔵 Upcoming applications (next 30 days)
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

        private static decimal? GetRateForWorkerOnDate(AppDbContext db, int workerId, DateTime workDate)
        {
            var rate = db.WorkerRates
                .Where(r => r.WorkerId == workerId &&
                            workDate >= r.ValidFrom &&
                            (r.ValidTo == null || workDate < r.ValidTo))
                .OrderByDescending(r => r.ValidFrom)
                .FirstOrDefault();

            return rate?.RatePerHour;
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
            var today = (todayOverride ?? DateTime.Today).Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var workers = db.Workers.ToList();
            var rates = db.WorkerRates.ToList();
            var entries = db.TimesheetEntries.ToList();

            // Last worked date per worker (all time)
            var lastWorkedByWorkerId = entries
                .GroupBy(e => e.WorkerId)
                .ToDictionary(g => g.Key, g => (DateTime?)g.Max(x => x.Date));

            // Hours this month per worker
            var monthEntries = entries.Where(e => e.Date.Date >= monthStart && e.Date.Date <= today).ToList();

            var hoursThisMonthByWorkerId = monthEntries
                .GroupBy(e => e.WorkerId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));

            // Cost this month: sum(entry hours * rate on that date)
            decimal GetRateForWorkerOnDate(int workerId, DateTime workDate)
            {
                var rate = rates
                    .Where(r => r.WorkerId == workerId &&
                                workDate >= r.ValidFrom &&
                                (r.ValidTo == null || workDate < r.ValidTo))
                    .OrderByDescending(r => r.ValidFrom)
                    .FirstOrDefault();

                return rate?.RatePerHour ?? 0m;
            }

            var costThisMonthByWorkerId = monthEntries
                .GroupBy(e => e.WorkerId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(e => e.Hours * GetRateForWorkerOnDate(e.WorkerId, e.Date)));

            decimal? GetCurrentRate(int workerId)
            {
                var current = rates
                    .Where(r => r.WorkerId == workerId && (r.ValidTo == null || r.ValidTo >= today))
                    .OrderByDescending(r => r.ValidFrom)
                    .FirstOrDefault();

                return current?.RatePerHour;
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
            var from = fromInclusive?.Date;
            var to = toInclusive?.Date;

            var entriesQuery = db.TimesheetEntries.Where(e => e.ProjectId == projectId);

            if (from.HasValue)
                entriesQuery = entriesQuery.Where(e => e.Date.Date >= from.Value);

            if (to.HasValue)
                entriesQuery = entriesQuery.Where(e => e.Date.Date <= to.Value);

            var entries = entriesQuery.ToList();
            if (!entries.Any())
                return new List<ProjectLabourByPersonRow>();

            var workersById = db.Workers.ToDictionary(w => w.Id);

            decimal GetRateOn(int workerId, DateTime date)
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
                .GroupBy(e => e.WorkerId)
                .Select(g =>
                {
                    var worker = workersById.TryGetValue(g.Key, out var w) ? w : null;
                    var initials = worker?.Initials ?? "";
                    var name = worker?.Name ?? $"WorkerId {g.Key}";

                    var hours = g.Sum(x => x.Hours);
                    var cost = g.Sum(x => x.Hours * GetRateOn(x.WorkerId, x.Date));

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
            var workersById = db.Workers.ToDictionary(w => w.Id);

            decimal GetRateOn(int workerId, DateTime date)
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
                .Where(e => e.ProjectId == projectId)
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.Id)
                .Take(take)
                .ToList();

            return entries.Select(e =>
            {
                var worker = workersById.TryGetValue(e.WorkerId, out var w) ? w : null;
                var initials = worker?.Initials ?? "";

                return new ProjectRecentEntryRow(
                    Date: e.Date.Date,
                    WorkerInitials: initials,
                    Hours: e.Hours,
                    Cost: e.Hours * GetRateOn(e.WorkerId, e.Date),
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
            var today = DateTime.Today;

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
            var today = DateTime.Today;
            var horizon = today.AddDays(daysAhead);
            var labourPeriodStart = new DateTime(today.Year, today.Month, 1);

            var result = new List<UpcomingApplicationEntry>();

            // Cache projects so we can build labels and check labour
            var projects = db.Projects.ToList();
            var timesheets = db.TimesheetEntries.ToList();

            bool HasRecentLabourForProjectCode(string code)
            {
                var projectIds = projects
                    .Where(p => p.JobNameOrNumber.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Id)
                    .ToHashSet();

                if (!projectIds.Any())
                    return false;

                return timesheets.Any(e =>
                    projectIds.Contains(e.ProjectId) &&
                    e.Date >= labourPeriodStart);
            }

            string GetProjectLabel(string code)
            {
                var proj = projects
                    .FirstOrDefault(p => p.JobNameOrNumber.StartsWith(code, StringComparison.OrdinalIgnoreCase));

                return proj?.JobNameOrNumber ?? code;
            }

            var schedules = db.ApplicationSchedules.ToList();

            // 🔵 1) Fixed rows: literal ApplicationSubmissionDate
            foreach (var sched in schedules.Where(s =>
                            s.ScheduleType.Equals("Fixed", StringComparison.OrdinalIgnoreCase) &&
                            s.ApplicationSubmissionDate.HasValue))
            {
                var date = sched.ApplicationSubmissionDate!.Value.Date;
                var daysUntil = (date - today).Days;
                if (date < today || date > horizon)
                    continue;

                if (!HasRecentLabourForProjectCode(sched.ProjectCode))
                    continue;

                var label = GetProjectLabel(sched.ProjectCode);

                result.Add(new UpcomingApplicationEntry(
                    ProjectCode: sched.ProjectCode,
                    ProjectLabel: label,
                    ApplicationDate: date,
                    DaysUntil: daysUntil,
                    ScheduleType: sched.ScheduleType,
                    RuleType: null,
                    Notes: sched.Notes));
            }

            // 🔵 2) Rule rows: expand into dates within [today, horizon]
            foreach (var sched in schedules.Where(s =>
                            s.ScheduleType.Equals("Rule", StringComparison.OrdinalIgnoreCase)))
            {
                var code = sched.ProjectCode;
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
                    var cursor = new DateTime(today.Year, today.Month, 1);
                    while (cursor <= horizon)
                    {
                        var lastDay = new DateTime(cursor.Year, cursor.Month,
                            DateTime.DaysInMonth(cursor.Year, cursor.Month));
                        var daysUntil = (lastDay.Date - today).Days;

                        if (lastDay >= today && lastDay <= horizon)
                        {
                            result.Add(new UpcomingApplicationEntry(
                                ProjectCode: code,
                                ProjectLabel: label,
                                ApplicationDate: lastDay.Date,
                                DaysUntil: daysUntil,
                                ScheduleType: sched.ScheduleType,
                                RuleType: sched.RuleType,
                                Notes: sched.Notes));
                        }

                        cursor = cursor.AddMonths(1);
                    }
                }
                else if (ruleType.Equals("SpecificDay", StringComparison.OrdinalIgnoreCase)
                            && sched.RuleValue.HasValue)
                {
                    int day = sched.RuleValue.Value;
                    if (day < 1) day = 1;

                    var cursor = new DateTime(today.Year, today.Month, 1);
                    while (cursor <= horizon)
                    {
                        int daysInMonth = DateTime.DaysInMonth(cursor.Year, cursor.Month);
                        int safeDay = Math.Min(day, daysInMonth);
                        var d = new DateTime(cursor.Year, cursor.Month, safeDay);
                        var daysUntil = (d.Date - today).Days;

                        if (d >= today && d <= horizon)
                        {
                            result.Add(new UpcomingApplicationEntry(
                                ProjectCode: code,
                                ProjectLabel: label,
                                ApplicationDate: d.Date,
                                DaysUntil: daysUntil,
                                ScheduleType: sched.ScheduleType,
                                RuleType: sched.RuleType,
                                Notes: sched.Notes));
                        }

                        cursor = cursor.AddMonths(1);
                    }
                }
            }

            // Remove duplicates & sort
            var distinct = result
                .GroupBy(r => new { r.ProjectCode, r.ApplicationDate })
                .Select(g => g.First())
                .OrderBy(r => r.ApplicationDate)
                .ThenBy(r => r.ProjectCode)
                .ToList();

            return distinct;
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
                Status: i.Status
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

            return invoices.Select(i => new InvoiceListEntry(
                Id: i.Id,
                InvoiceNumber: i.InvoiceNumber,
                ProjectCode: i.ProjectCode,
                JobName: i.JobName,
                ClientName: i.ClientName,
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
            )).ToList();
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
    }
}
