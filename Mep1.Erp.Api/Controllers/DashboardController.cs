using Mep1.Erp.Api.Security;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Mep1.Erp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOrOwner")]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(AppDbContext db, ILogger<DashboardController> logger)
        {
            _db = db;
            _logger = logger;
        }

        private (int WorkerId, string Role, string Source) GetActorForAudit()
        {
            var id = ClaimsActor.GetWorkerId(User);
            var role = ClaimsActor.GetRole(User);
            return (id, role.ToString(), GetClientApp());
        }

        private string GetClientApp()
        {
            var kind = HttpContext.Items["ApiKeyKind"] as string;
            return string.Equals(kind, "Admin", StringComparison.Ordinal) ? "Desktop" : "Portal";
        }

        // GET /api/dashboard/summary?daysAhead=30
        [HttpGet("summary")]
        public ActionResult<DashboardSummaryDto> GetSummary([FromQuery] int? daysAhead = null)
        {
            try
            {
                var settings = new AppSettings
                {
                    UpcomingApplicationsDaysAhead = daysAhead ?? 30
                };

                var summary = Reporting.GetDashboardSummary(_db, settings);

                // Applications (cashflow pipeline)
                // Rules:
                // - Exclude Draft by only including Submitted/Applied/Agreed
                // - Exclude anything already invoiced (Invoice.ApplicationId points to Application.Id)
                // - Values are NET
                var invoicedAppIds = _db.Invoices
                    .AsNoTracking()
                    .Where(i => i.ApplicationId != null)
                    .Select(i => i.ApplicationId!.Value);

                var appsBase = _db.Applications
                    .AsNoTracking()
                    .Where(a => !invoicedAppIds.Contains(a.Id));

                // Normalize status in a SQL-translatable way (EF can translate ToLower())
                var appliedQuery = appsBase.Where(a =>
                    a.Status != null &&
                    (a.Status.ToLower() == "submitted" || a.Status.ToLower() == "applied"));

                var applicationsAppliedNotInvoicedNet = appliedQuery.Sum(a => (decimal?)a.SubmittedNetAmount) ?? 0m;
                var applicationsAppliedNotInvoicedCount = appliedQuery.Count();

                var agreedQuery = appsBase.Where(a =>
                    a.Status != null &&
                    a.Status.ToLower() == "agreed");

                var applicationsAgreedReadyToInvoiceNet = agreedQuery
                    .Sum(a => (decimal?)(a.AgreedNetAmount ?? a.SubmittedNetAmount)) ?? 0m;

                var applicationsAgreedReadyToInvoiceCount = agreedQuery.Count();

                return Ok(new DashboardSummaryDto
                {
                    OutstandingNet = summary.OutstandingNet,
                    OutstandingGross = summary.OutstandingGross,
                    UnpaidInvoiceCount = summary.UnpaidInvoiceCount,
                    ActiveProjects = summary.ActiveProjects,
                    LatestTimesheetDate = summary.LatestTimesheetDate,
                    LatestInvoiceDate = summary.LatestInvoiceDate,
                    OverdueInvoiceCount = summary.OverdueInvoiceCount,
                    OverdueOutstandingNet = summary.OverdueOutstandingNet,
                    DueNext30DaysCount = summary.DueNext30DaysCount,
                    DueNext30DaysOutstandingNet = summary.DueNext30DaysOutstandingNet,
                    NextDueDate = summary.NextDueDate,
                    UpcomingApplicationCount = summary.UpcomingApplicationCount,
                    NextApplicationDate = summary.NextApplicationDate,
                    ApplicationsAppliedNotInvoicedNet = applicationsAppliedNotInvoicedNet,
                    ApplicationsAppliedNotInvoicedCount = applicationsAppliedNotInvoicedCount,
                    ApplicationsAgreedReadyToInvoiceNet = applicationsAgreedReadyToInvoiceNet,
                    ApplicationsAgreedReadyToInvoiceCount = applicationsAgreedReadyToInvoiceCount,
                    OverdueOutstandingGross = summary.OverdueOutstandingGross,
                    DueNext30DaysOutstandingGross = summary.DueNext30DaysOutstandingGross
                });
            }
            catch (Exception ex) 
            {
                // Log the real error to server logs
                _logger.LogError(ex, "Dashboard summary failed. daysAhead={daysAhead}", daysAhead);

                // Return a useful error shape (still 500, but with detail you can see)
                return Problem(title: "Dashboard summary failed", detail: ex.Message);
            }
        }

        // GET /api/dashboard/due-schedule
        [HttpGet("due-schedule")]
        public ActionResult<List<DueScheduleEntryDto>> GetDueSchedule()
        {
            var rows = Reporting.GetDueSchedule(_db);

            var dto = rows.Select(r => new DueScheduleEntryDto
            {
                WeekStart = r.WeekStart,
                WeekEnd = r.WeekEnd,
                OutstandingNet = r.OutstandingNet,
                InvoiceCount = r.InvoiceCount
            }).ToList();

            return Ok(dto);
        }

        // GET /api/dashboard/upcoming-applications?daysAhead=30
        [HttpGet("upcoming-applications")]
        public ActionResult<List<UpcomingApplicationEntryDto>> GetUpcomingApplications([FromQuery] int? daysAhead = null)
        {
            var horizon = daysAhead ?? 30;

            var rows = Reporting.GetUpcomingApplications(_db, horizon)
                .OrderBy(a => a.ApplicationDate)
                .ThenBy(a => a.ProjectCode)
                .ToList();

            var dto = rows.Select(a => new UpcomingApplicationEntryDto
            {
                ProjectCode = a.ProjectCode,
                ProjectLabel = a.ProjectLabel,
                ApplicationDate = a.ApplicationDate,
                DaysUntil = a.DaysUntil,
                ScheduleType = a.ScheduleType,
                RuleType = a.RuleType,
                Notes = a.Notes
            }).ToList();

            return Ok(dto);
        }
    }
}
