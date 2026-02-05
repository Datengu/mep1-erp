using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

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
                ApplicationId = r.ApplicationId,
                ApplicationNumber = r.ApplicationNumber,
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

        public sealed record BackfillInvoiceProjectLinksResult(
            bool DryRun,
            int TotalCandidates,
            int Updated,
            int SkippedAlreadyLinked,
            int SkippedNoMatch,
            int SkippedAmbiguous,
            List<BackfillInvoiceProjectLinksItem> Items
        );

        public sealed record BackfillInvoiceProjectLinksItem(
            int InvoiceId,
            string InvoiceNumber,
            string InvoiceProjectCode,
            string Outcome,
            int? MatchedProjectId,
            string? MatchedProjectLabel,
            string? Note
        );

        // Admin maintenance: backfill Invoice.ProjectId from Invoice.ProjectCode by matching Projects base codes.
        // Dry-run by default (safe). Use dryRun=false to apply.
        [HttpPost("admin/backfill-project-links")]
        public async Task<ActionResult<BackfillInvoiceProjectLinksResult>> BackfillInvoiceProjectLinks(
            [FromQuery] bool dryRun = true,
            [FromQuery] int take = 5000)
        {
            if (take < 1) take = 1;
            if (take > 50000) take = 50000;

            // Build lookup: BaseCode -> projects that share it (ambiguous codes are handled safely)
            var projects = await _db.Projects
                .AsNoTracking()
                .Select(p => new { p.Id, p.JobNameOrNumber })
                .ToListAsync();

            var projectsByBaseCode = new Dictionary<string, List<(int Id, string Label)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in projects)
            {
                var baseCode = ProjectCodeHelpers.GetBaseProjectCode(p.JobNameOrNumber)?.Trim();
                if (string.IsNullOrWhiteSpace(baseCode))
                    continue;

                if (!projectsByBaseCode.TryGetValue(baseCode, out var list))
                {
                    list = new List<(int, string)>();
                    projectsByBaseCode[baseCode] = list;
                }

                list.Add((p.Id, p.JobNameOrNumber));
            }

            // Candidates: invoices with no ProjectId but with a project code
            var invoices = await _db.Invoices
                .Where(i => i.ProjectId == null && i.ProjectCode != null && i.ProjectCode != "")
                .OrderBy(i => i.Id)
                .Take(take)
                .ToListAsync();

            var items = new List<BackfillInvoiceProjectLinksItem>(capacity: Math.Min(invoices.Count, 2000));

            int updated = 0;
            int skippedAlreadyLinked = 0;
            int skippedNoMatch = 0;
            int skippedAmbiguous = 0;

            foreach (var inv in invoices)
            {
                // Defensive trimming
                var invCode = (inv.ProjectCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(invCode))
                {
                    skippedNoMatch++;
                    if (items.Count < 2000)
                    {
                        items.Add(new BackfillInvoiceProjectLinksItem(
                            InvoiceId: inv.Id,
                            InvoiceNumber: inv.InvoiceNumber,
                            InvoiceProjectCode: invCode,
                            Outcome: "Skipped",
                            MatchedProjectId: null,
                            MatchedProjectLabel: null,
                            Note: "Invoice.ProjectCode was empty after trimming."
                        ));
                    }
                    continue;
                }

                if (inv.ProjectId.HasValue)
                {
                    skippedAlreadyLinked++;
                    continue;
                }

                if (!projectsByBaseCode.TryGetValue(invCode, out var matches) || matches.Count == 0)
                {
                    skippedNoMatch++;
                    if (items.Count < 2000)
                    {
                        items.Add(new BackfillInvoiceProjectLinksItem(
                            InvoiceId: inv.Id,
                            InvoiceNumber: inv.InvoiceNumber,
                            InvoiceProjectCode: invCode,
                            Outcome: "NoMatch",
                            MatchedProjectId: null,
                            MatchedProjectLabel: null,
                            Note: "No Project matched this base code."
                        ));
                    }
                    continue;
                }

                if (matches.Count > 1)
                {
                    skippedAmbiguous++;
                    if (items.Count < 2000)
                    {
                        var note = "Ambiguous base code. Matches: " + string.Join(" | ", matches.Select(m => $"{m.Id}:{m.Label}"));
                        items.Add(new BackfillInvoiceProjectLinksItem(
                            InvoiceId: inv.Id,
                            InvoiceNumber: inv.InvoiceNumber,
                            InvoiceProjectCode: invCode,
                            Outcome: "Ambiguous",
                            MatchedProjectId: null,
                            MatchedProjectLabel: null,
                            Note: note
                        ));
                    }
                    continue;
                }

                // Exactly one match -> safe to link
                var match = matches[0];

                if (!dryRun)
                {
                    inv.ProjectId = match.Id;

                    // Optional normalization: keep invoice strings aligned to the canonical Project
                    inv.JobName = match.Label;

                    // Ensure stored ProjectCode is the base code derived from the canonical Project label
                    // (prevents drift if legacy data had weird spacing/dashes)
                    var canonicalBase = ProjectCodeHelpers.GetBaseProjectCode(match.Label)?.Trim();
                    if (!string.IsNullOrWhiteSpace(canonicalBase))
                        inv.ProjectCode = canonicalBase;
                }

                updated++;
                if (items.Count < 2000)
                {
                    items.Add(new BackfillInvoiceProjectLinksItem(
                        InvoiceId: inv.Id,
                        InvoiceNumber: inv.InvoiceNumber,
                        InvoiceProjectCode: invCode,
                        Outcome: dryRun ? "WouldUpdate" : "Updated",
                        MatchedProjectId: match.Id,
                        MatchedProjectLabel: match.Label,
                        Note: null
                    ));
                }
            }

            if (!dryRun)
            {
                await _db.SaveChangesAsync();

                // One audit entry (not per invoice)
                await _audit.LogAsync(
                    action: "Invoice.BackfillProjectLinks",
                    entityType: "Invoice",
                    entityId: "(bulk)",
                    summary: $"Backfilled ProjectId for {updated} invoices (take={take}).",
                    actorWorkerId: GetActorForAudit().WorkerId,
                    subjectWorkerId: GetActorForAudit().WorkerId,
                    actorRole: GetActorForAudit().Role,
                    actorSource: GetActorForAudit().Source
                );
            }

            var result = new BackfillInvoiceProjectLinksResult(
                DryRun: dryRun,
                TotalCandidates: invoices.Count,
                Updated: updated,
                SkippedAlreadyLinked: skippedAlreadyLinked,
                SkippedNoMatch: skippedNoMatch,
                SkippedAmbiguous: skippedAmbiguous,
                Items: items
            );

            return Ok(result);
        }

    }
}
