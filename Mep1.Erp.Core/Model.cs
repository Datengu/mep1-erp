using System;
using System.Collections.Generic;

namespace Mep1.Erp.Core
{
    public class Worker
    {
        public int Id { get; set; }
        public string Initials { get; set; } = "";
        public string Name { get; set; } = "";
        public List<TimesheetEntry> TimesheetEntries { get; set; } = new();

        // rate history
        public List<WorkerRate> Rates { get; set; } = new();

        public string? SignatureName { get; set; }              // typed name used as “signature”
        public DateTime? SignatureCapturedAtUtc { get; set; }   // when they first set it

        public bool IsActive { get; set; } = true;
    }

    public class Project
    {
        public int Id { get; set; }

        public string JobNameOrNumber { get; set; } = ""; // e.g. "PN0049 - Biggin Hill"
        public string Company { get; set; } = "";         // e.g. "PAV"

        public string Category { get; set; } = "Uncategorised";
        public bool IsRealProject { get; set; } = true;
        public bool IsActive { get; set; } = true;

        public List<TimesheetEntry> TimesheetEntries { get; set; } = new();

        public List<SupplierCost> SupplierCosts { get; set; } = new();
    }

    public class TimesheetEntry
    {
        public int Id { get; set; }
        // stable identity per worker
        public int EntryId { get; set; }
        public DateTime Date { get; set; }
        public decimal Hours { get; set; }

        public string Code { get; set; } = "";

        public string TaskDescription { get; set; } = "";
        public string CcfRef { get; set; } = "";

        public int WorkerId { get; set; }
        public Worker Worker { get; set; } = null!;

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        // --- Audit / lifecycle metadata (Option A) ---
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public int? UpdatedByWorkerId { get; set; }

        // --- Soft delete ---
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAtUtc { get; set; }
        public int? DeletedByWorkerId { get; set; }

        // --- New for tracking hours worked more specifically on what ---
        public string WorkType { get; set; } = "M";   // "S" or "M"

        // Stored as JSON in DB
        public string LevelsJson { get; set; } = "[]";
        public string AreasJson { get; set; } = "[]";

    }

    public class WorkerRate
    {
        public int Id { get; set; }

        public int WorkerId { get; set; }
        public Worker Worker { get; set; } = null!;

        public decimal RatePerHour { get; set; }

        // Start of this rate being valid (inclusive)
        public DateTime ValidFrom { get; set; }

        // Optional end date (null = current rate)
        public DateTime? ValidTo { get; set; }
    }

    public class Supplier
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public string? Notes { get; set; }

        public List<SupplierCost> SupplierCosts { get; set; } = new();
    }

    public class SupplierCost
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        public DateTime? Date { get; set; }
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }

    public class Invoice
    {
        public int Id { get; set; }

        // From "InvoiceNo"
        public string InvoiceNumber { get; set; } = null!;

        // From "JobNo" (e.g. PN0051)
        public string ProjectCode { get; set; } = null!;

        // From "JobName" – optional, handy for sanity-checking
        public string? JobName { get; set; }

        // From "ClientName"
        public string? ClientName { get; set; }

        // From "InvoiceDate"
        public DateTime InvoiceDate { get; set; }

        // From "PaymentTermsDays" (nullable)
        public int? PaymentTermsDays { get; set; }

        // From "DueDate"
        public DateTime? DueDate { get; set; }

        // From "NetAmount"
        public decimal NetAmount { get; set; }

        // From "VATRate"
        public decimal? VatRate { get; set; }

        // From "VATAmount"
        public decimal? VatAmount { get; set; }

        // From "GrossAmount"
        public decimal? GrossAmount { get; set; }

        // From "PaymentAmount"
        public decimal? PaymentAmount { get; set; }

        // From "DatePaid"
        public DateTime? PaidDate { get; set; }

        // From "Status"
        public string? Status { get; set; }

        // From "FilePath"
        public string? FilePath { get; set; }

        // From "Notes"
        public string? Notes { get; set; }

        // Convenience flag derived from PaymentAmount / Status
        public bool IsPaid { get; set; }

        // Optional link to Project later if/when we normalise project codes
        public int? ProjectId { get; set; }
        public Project? Project { get; set; }
    }

    public class ApplicationSchedule
    {
        public int Id { get; set; }

        // From "ProjectNo" in your Application Schedule workbook (e.g. "PN0049")
        public string ProjectCode { get; set; } = "";

        // "Fixed" or "Rule"
        public string ScheduleType { get; set; } = "";

        // For Fixed rows
        public DateTime? ApplicationSubmissionDate { get; set; }
        public DateTime? ValuationPeriodEnd { get; set; }
        public DateTime? PaymentDueDate { get; set; }
        public DateTime? PaymentNoticeDueDate { get; set; }
        public DateTime? PayLessNoticeDueDate { get; set; }
        public DateTime? FinalPaymentDate { get; set; }

        // For Rule rows
        // e.g. "EndOfMonth", "SpecificDay", "OnCompletion", "Unknown"
        public string? RuleType { get; set; }
        public int? RuleValue { get; set; } // e.g. 23 for SpecificDay = 23rd

        public string? Notes { get; set; }
    }
}
