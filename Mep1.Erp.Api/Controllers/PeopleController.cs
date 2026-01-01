using Microsoft.AspNetCore.Mvc;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class PeopleController : ControllerBase
    {
        [HttpGet("summary")]
        public ActionResult<List<PeopleSummaryRowDto>> GetPeopleSummary()
        {
            using var db = new AppDbContext();

            var rows = Reporting.GetPeopleSummary(db);

            var dto = rows.Select(r => new PeopleSummaryRowDto
            {
                WorkerId = r.WorkerId,
                Initials = r.Initials,
                Name = r.Name,
                CurrentRatePerHour = r.CurrentRatePerHour,
                LastWorkedDate = r.LastWorkedDate,
                HoursThisMonth = r.HoursThisMonth,
                CostThisMonth = r.CostThisMonth
            }).ToList();

            return Ok(dto);
        }

        [HttpGet("{workerId:int}/drilldown")]
        public ActionResult<PersonDrilldownDto> GetPersonDrilldown(int workerId)
        {
            using var db = new AppDbContext();

            var today = DateTime.Today.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var rates = Reporting.GetWorkerRateHistory(db, workerId)
                .Select(r => new WorkerRateDto
                {
                    ValidFrom = r.ValidFrom,
                    ValidTo = r.ValidTo,
                    RatePerHour = r.RatePerHour
                })
                .ToList();

            var projects = Reporting.GetPersonProjectBreakdown(db, workerId, monthStart, today)
                .Select(p => new PersonProjectBreakdownRowDto
                {
                    ProjectLabel = p.ProjectLabel,
                    ProjectCode = p.ProjectCode,
                    Hours = p.Hours,
                    Cost = p.Cost
                })
                .ToList();

            var recent = Reporting.GetPersonRecentEntries(db, workerId, take: 20)
                .Select(e => new PersonRecentEntryRowDto
                {
                    Date = e.Date,
                    ProjectLabel = e.ProjectLabel,
                    ProjectCode = e.ProjectCode,
                    Hours = e.Hours,
                    TaskDescription = e.TaskDescription,
                    Cost = e.Cost
                })
                .ToList();

            return Ok(new PersonDrilldownDto
            {
                Rates = rates,
                Projects = projects,
                RecentEntries = recent
            });
        }
    }
}
