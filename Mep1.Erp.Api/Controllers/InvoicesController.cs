using Microsoft.AspNetCore.Mvc;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class InvoicesController : ControllerBase
    {
        [HttpGet]
        public ActionResult<List<InvoiceListEntryDto>> GetInvoices()
        {
            using var db = new AppDbContext();

            var rows = Reporting.GetInvoiceList(db);

            var dto = rows.Select(r => new InvoiceListEntryDto
            {
                Id = r.Id,
                InvoiceNumber = r.InvoiceNumber,
                ProjectCode = r.ProjectCode,
                JobName = r.JobName,
                ClientName = r.ClientName,
                InvoiceDate = r.InvoiceDate,
                DueDate = r.DueDate,
                NetAmount = r.NetAmount,
                GrossAmount = r.GrossAmount,
                PaymentAmount = r.PaymentAmount,
                PaidDate = r.PaidDate,
                Status = r.Status,
                IsPaid = r.IsPaid,
                OutstandingNet = r.OutstandingNet,
                OutstandingGross = r.OutstandingGross,
                FilePath = r.FilePath,
                Notes = r.Notes
            }).ToList();

            return Ok(dto);
        }
    }
}
