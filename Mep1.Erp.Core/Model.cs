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

    public class Company
    {
        public int Id { get; set; }

        // Your current values look like short codes: "PAV", "RDX", "ESG", etc.
        public string Code { get; set; } = "";

        // Optional nice-to-have (can be same as Code for now)
        public string Name { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public List<Project> Projects { get; set; } = new();
    }

    public class Project
    {
        public int Id { get; set; }

        public string JobNameOrNumber { get; set; } = ""; // e.g. "PN0049 - Biggin Hill"

        public int? CompanyId { get; set; }
        public Company? CompanyEntity { get; set; }

        public string Category { get; set; } = "Uncategorised";
        public bool IsRealProject { get; set; } = true;
        public bool IsActive { get; set; } = true;

        public List<TimesheetEntry> TimesheetEntries { get; set; } = new();

        public List<SupplierCost> SupplierCosts { get; set; } = new();
    }

    public class ProjectCcfRef
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        // Always stored normalized: "001" .. "999"
        public string Code { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // --- Soft delete ---
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAtUtc { get; set; }
        public int? DeletedByWorkerId { get; set; }

        // ---- Commercial values ----

        // Early internal estimate
        public decimal? EstimatedValue { get; set; }

        // What you submit / quote to the client
        public decimal? QuotedValue { get; set; }
        public DateTime? QuotedDateUtc { get; set; }

        // What the client agrees to
        public decimal? AgreedValue { get; set; }
        public DateTime? AgreedDateUtc { get; set; }

        // What is actually invoiced / recognised
        public decimal? ActualValue { get; set; }

        // ---- Status & metadata ----

        // Current commercial status of this CCF
        // e.g. "Draft", "Quoted", "Agreed", "Rejected", "Invoiced"
        public string Status { get; set; } = "Draft";

        // Optional notes / justification
        public string? Notes { get; set; }

        // Tracking
        public DateTime? LastValueUpdatedUtc { get; set; }
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

        public int? ProjectCcfRefId { get; set; }
        public ProjectCcfRef? ProjectCcfRef { get; set; }

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
        public string? WorkType { get; set; }   // "S" or "M" or null

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

        // Optional link to an Application (real submission/certification record)
        // Nullable so existing invoices remain valid until you back-fill applications.
        public int? ApplicationId { get; set; }
        public Application? Application { get; set; }
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

    public class Application
    {
        public int Id { get; set; }

        // Project code (e.g. PN0049). Mirrors Invoice.ProjectCode pattern.
        public string ProjectCode { get; set; } = null!;

        // Per-project sequence number (1, 2, 3...).
        public int ApplicationNumber { get; set; }

        // When you submitted the application.
        public DateTime DateApplied { get; set; }

        // Money requested at submission (net).
        public decimal SubmittedNetAmount { get; set; }

        // Money agreed/certified (net). Nullable until agreed.
        public decimal? AgreedNetAmount { get; set; }

        // When it was agreed/certified (if applicable).
        public DateTime? DateAgreed { get; set; }

        // Client / contract admin reference, certificate number etc.
        public string? ExternalReference { get; set; }

        // Draft / Submitted / Agreed / Invoiced / Cancelled etc (keep string like your other models).
        public string Status { get; set; } = "Submitted";

        public string? Notes { get; set; }

        // Optional link to Project later if/when you normalise project codes
        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        // Navigation back to invoice (1:1 optional)
        public Invoice? Invoice { get; set; }
    }
}
