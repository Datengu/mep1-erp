using Mep1.Erp.Api.Security;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace Mep1.Erp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOrOwner")]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _db;

        public DashboardController(AppDbContext db)
        {
            _db = db;
        }

        private bool IsAdminKey()
            => string.Equals(HttpContext.Items["ApiKeyKind"] as string, "Admin", StringComparison.Ordinal);

        private ActionResult? RequireAdminKey()
        {
            if (IsAdminKey()) return null;
            return Unauthorized("Admin API key required.");
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
            var guard = RequireAdminKey();
            if (guard != null) return guard;

            var settings = new AppSettings
            {
                UpcomingApplicationsDaysAhead = daysAhead ?? 30
            };

            var summary = Reporting.GetDashboardSummary(_db, settings);

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
                NextApplicationDate = summary.NextApplicationDate
            });
        }

        // GET /api/dashboard/due-schedule
        [HttpGet("due-schedule")]
        public ActionResult<List<DueScheduleEntryDto>> GetDueSchedule()
        {
            var guard = RequireAdminKey();
            if (guard != null) return guard;

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
            var guard = RequireAdminKey();
            if (guard != null) return guard;

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
