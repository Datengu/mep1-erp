using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mep1.Erp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOrOwner")]
    public sealed class InvoicesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly AuditLogger _audit;

        public InvoicesController(AppDbContext db, AuditLogger audit)
        {
            _db = db;
            _audit = audit;
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

        private static bool TryParseRole(string role, out TimesheetUserRole parsed)
            => Enum.TryParse<TimesheetUserRole>(role, ignoreCase: true, out parsed);

        [HttpGet]
        public ActionResult<List<InvoiceListEntryDto>> GetInvoices()
        {
            var rows = Reporting.GetInvoiceList(_db);

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

        private static string NormalizeInvoiceNumber(string? raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return raw;

            // Lowercase suffix to match historical data (0507A -> 0507a)
            raw = raw.ToLowerInvariant();

            // Pad leading numeric prefix to 4 digits (603 -> 0603, 507a -> 0507a)
            int i = 0;
            while (i < raw.Length && char.IsDigit(raw[i])) i++;

            if (i == 0) return raw;

            var prefix = raw.Substring(0, i);
            var suffix = raw.Substring(i);

            if (!int.TryParse(prefix, out var n)) return raw;

            return n.ToString("D4") + suffix;
        }

        private static DateTime AsUtcDate(DateTime d)
        {
            // dto.InvoiceDate.Date etc produces Kind=Unspecified; Postgres timestamptz requires UTC
            return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
        }

        private static DateTime? AsUtcDate(DateTime? d)
        {
            if (!d.HasValue) return null;
            return DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc);
        }

        [HttpPost]
        public async Task<ActionResult<CreateInvoiceResponseDto>> Create([FromBody] CreateInvoiceRequestDto dto)
        {
            var invoiceNo = NormalizeInvoiceNumber(dto.InvoiceNumber);
            if (string.IsNullOrWhiteSpace(invoiceNo))
                return BadRequest("Invoice number is required.");

            var exists = await _db.Invoices
                .AsNoTracking()
                .AnyAsync(i => i.InvoiceNumber == invoiceNo);

            if (exists)
                return Conflict($"Invoice '{invoiceNo}' already exists.");

            // Project must exist + must have company linked
            var project = await _db.Projects
                .Include(p => p.CompanyEntity)
                .FirstOrDefaultAsync(p => p.Id == dto.ProjectId);

            if (project == null)
                return BadRequest("Project not found.");

            if (project.CompanyId == null || project.CompanyEntity == null)
                return BadRequest("Selected project has no company linked.");

            if (dto.NetAmount <= 0m)
                return BadRequest("Net amount must be > 0.");

            var vatRate = dto.VatRate;
            if (vatRate < 0m || vatRate > 1m)
                return BadRequest("VAT rate must be between 0 and 1 (e.g. 0.2 for 20%).");

            var invoiceDate = AsUtcDate(dto.InvoiceDate);
            var dueDate = AsUtcDate(dto.DueDate);

            var vatAmount = Math.Round(dto.NetAmount * vatRate, 2, MidpointRounding.AwayFromZero);
            var grossAmount = dto.NetAmount + vatAmount;

            // Fill legacy/string fields for compatibility with your current invoice list/grid.
            var projectCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
            var jobName = project.JobNameOrNumber;
            var clientName = project.CompanyEntity!.Name;

            var status = string.IsNullOrWhiteSpace(dto.Status) ? "Outstanding" : dto.Status.Trim();
            var isPaid = string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase);

            var entity = new Invoice
            {
                InvoiceNumber = invoiceNo,

                ProjectId = project.Id,
                ProjectCode = projectCode,
                JobName = jobName,
                ClientName = clientName,

                InvoiceDate = invoiceDate,
                DueDate = dueDate,

                NetAmount = dto.NetAmount,
                VatRate = vatRate,
                VatAmount = vatAmount,
                GrossAmount = grossAmount,

                Status = status,
                IsPaid = isPaid,
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
            };

            _db.Invoices.Add(entity);
            await _db.SaveChangesAsync();

            var a = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: a.WorkerId,
                subjectWorkerId: a.WorkerId,
                actorRole: a.Role,
                actorSource: a.Source,
                action: "Invoice.Create",
                entityType: "Invoice",
                entityId: entity.Id.ToString(),
                summary: $"{entity.InvoiceNumber} {entity.NetAmount:0.00}net {entity.Status}"
            );

            var resp = new CreateInvoiceResponseDto
            {
                Id = entity.Id,
                InvoiceNumber = entity.InvoiceNumber,

                ProjectId = project.Id,
                ProjectCode = projectCode,
                JobNameOrNumber = jobName,
                CompanyName = clientName,

                InvoiceDate = invoiceDate,
                DueDate = dueDate,

                NetAmount = dto.NetAmount,
                VatRate = vatRate,
                VatAmount = vatAmount,
                GrossAmount = grossAmount,

                Status = status,
                Notes = entity.Notes
            };

            return Created($"api/invoices/{entity.Id}", resp);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<InvoiceDetailsDto>> GetById(int id)
        {
            var invoice = await _db.Invoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null)
                return NotFound($"Invoice {id} not found.");

            // If invoice has ProjectId, prefer canonical info from Projects/Companies.
            string? companyName = invoice.ClientName;
            string? jobName = invoice.JobName;
            string projectCode = invoice.ProjectCode;

            if (invoice.ProjectId.HasValue)
            {
                var project = await _db.Projects
                    .Include(p => p.CompanyEntity)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == invoice.ProjectId.Value);

                if (project != null)
                {
                    jobName = project.JobNameOrNumber;
                    projectCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
                    companyName = project.CompanyEntity?.Name ?? companyName;
                }
            }

            var vatRate = invoice.VatRate ?? 0.20m;
            var (vatAmount, grossAmount) = ComputeVat(invoice.NetAmount, vatRate);

            var status = CleanStatus(invoice.Status);
            var isPaid = string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase);

            var dto = new InvoiceDetailsDto
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,

                ProjectId = invoice.ProjectId,
                ProjectCode = projectCode,
                JobName = jobName,
                CompanyName = companyName,

                InvoiceDate = invoice.InvoiceDate,
                DueDate = invoice.DueDate,

                NetAmount = invoice.NetAmount,
                VatRate = vatRate,
                VatAmount = vatAmount,
                GrossAmount = grossAmount,

                PaymentAmount = invoice.PaymentAmount,
                PaidDate = invoice.PaidDate,

                Status = status,
                IsPaid = isPaid,

                FilePath = invoice.FilePath,
                Notes = invoice.Notes
            };

            return Ok(dto);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<UpdateInvoiceResponseDto>> Update(int id, [FromBody] UpdateInvoiceRequestDto dto)
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null)
                return NotFound($"Invoice {id} not found.");

            // Validate
            if (dto.NetAmount <= 0m)
                return BadRequest("Net amount must be > 0.");

            if (dto.VatRate < 0m || dto.VatRate > 1m)
                return BadRequest("VAT rate must be between 0 and 1 (e.g. 0.2 for 20%).");

            // Optional project relink
            if (dto.ProjectId.HasValue)
            {
                var project = await _db.Projects
                    .Include(p => p.CompanyEntity)
                    .FirstOrDefaultAsync(p => p.Id == dto.ProjectId.Value);

                if (project == null)
                    return BadRequest("Project not found.");

                if (project.CompanyId == null || project.CompanyEntity == null)
                    return BadRequest("Selected project has no company linked.");

                invoice.ProjectId = project.Id;
                invoice.ProjectCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
                invoice.JobName = project.JobNameOrNumber;
                invoice.ClientName = project.CompanyEntity!.Name;
            }

            invoice.InvoiceDate = AsUtcDate(dto.InvoiceDate);
            invoice.DueDate = AsUtcDate(dto.DueDate);

            invoice.NetAmount = dto.NetAmount;
            invoice.VatRate = dto.VatRate;

            var (vatAmount, grossAmount) = ComputeVat(dto.NetAmount, dto.VatRate);
            invoice.VatAmount = vatAmount;
            invoice.GrossAmount = grossAmount;

            var status = CleanStatus(dto.Status);
            invoice.Status = status;
            invoice.IsPaid = string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase);

            invoice.PaymentAmount = dto.PaymentAmount;
            invoice.PaidDate = AsUtcDate(dto.PaidDate);

            invoice.FilePath = string.IsNullOrWhiteSpace(dto.FilePath) ? null : dto.FilePath.Trim();
            invoice.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

            await _db.SaveChangesAsync();

            var a = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: a.WorkerId,
                subjectWorkerId: a.WorkerId,
                actorRole: a.Role,
                actorSource: a.Source,
                action: "Invoice.Update",
                entityType: "Invoice",
                entityId: invoice.Id.ToString(),
                summary: $"{invoice.InvoiceNumber} {invoice.NetAmount:0.00}net {invoice.Status}"
            );

            var resp = new UpdateInvoiceResponseDto
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,

                ProjectId = invoice.ProjectId,
                ProjectCode = invoice.ProjectCode,
                JobName = invoice.JobName,
                CompanyName = invoice.ClientName,

                InvoiceDate = invoice.InvoiceDate,
                DueDate = invoice.DueDate,

                NetAmount = invoice.NetAmount,
                VatRate = invoice.VatRate ?? 0.20m,
                VatAmount = invoice.VatAmount ?? 0m,
                GrossAmount = invoice.GrossAmount ?? invoice.NetAmount,

                PaymentAmount = invoice.PaymentAmount,
                PaidDate = invoice.PaidDate,

                Status = invoice.Status,
                IsPaid = invoice.IsPaid,

                FilePath = invoice.FilePath,
                Notes = invoice.Notes
            };

            return Ok(resp);
        }

        private static (decimal VatAmount, decimal GrossAmount) ComputeVat(decimal net, decimal vatRate)
        {
            // Accounting-friendly rounding
            var vatAmount = Math.Round(net * vatRate, 2, MidpointRounding.AwayFromZero);
            var gross = net + vatAmount;
            return (vatAmount, gross);
        }

        private static string CleanStatus(string? s)
        {
            s = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? "Outstanding" : s;
        }

    }
}
