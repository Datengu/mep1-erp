using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mep1.Erp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class InvoicesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly AuditLogger _audit;

        public InvoicesController(AppDbContext db, AuditLogger audit)
        {
            _db = db;
            _audit = audit;
        }

        private bool IsAdminKey()
            => string.Equals(HttpContext.Items["ApiKeyKind"] as string, "Admin", StringComparison.Ordinal);

        private ActionResult? RequireAdminKey()
        {
            if (IsAdminKey()) return null;
            return Unauthorized("Admin API key required.");
        }

        private ActorContext? GetActor()
            => HttpContext.Items["Actor"] as ActorContext;

        private (int? WorkerId, string Role, string Source) GetActorForAudit()
        {
            var actor = GetActor();
            if (actor != null)
                return (actor.WorkerId, actor.Role.ToString(), "Desktop");

            // Admin API key used without an actor token (should be rare)
            return (null, "AdminKey", "ApiKey");
        }

        private ActionResult? RequireAdminActor()
        {
            if (!IsAdminKey())
                return Unauthorized("Admin API key required.");

            var actor = GetActor();
            if (actor is null)
                return Unauthorized("Actor token required.");

            if (!actor.IsAdminOrOwner)
                return Unauthorized("Admin/Owner actor required.");

            return null;
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

        [HttpPost]
        public async Task<ActionResult<CreateInvoiceResponseDto>> Create([FromBody] CreateInvoiceRequestDto dto)
        {
            // Follow your existing controller's auth style.
            // If your InvoicesController already has a guard method, keep using it.
            var guard = RequireAdminKey();
            if (guard != null) return guard;

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

            var invoiceDate = dto.InvoiceDate.Date;
            var dueDate = dto.DueDate.Date;

            var vatAmount = Math.Round(dto.NetAmount * vatRate, 2, MidpointRounding.AwayFromZero);
            var grossAmount = dto.NetAmount + vatAmount;

            // Fill legacy/string fields for compatibility with your current invoice list/grid.
            var projectCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
            var jobName = project.JobNameOrNumber;
            var clientName = project.CompanyEntity.Name;

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
    }
}
