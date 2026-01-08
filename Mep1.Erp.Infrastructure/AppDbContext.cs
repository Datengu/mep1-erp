using Microsoft.EntityFrameworkCore;
using System.IO;
using Mep1.Erp.Core;

namespace Mep1.Erp.Infrastructure
{
    public class AppDbContext : DbContext
    {
        // this constructor is required for AddDbContext(...) in the API
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // parameterless constructor so Desktop/Importer can still do `new AppDbContext()`
        public AppDbContext()
        {
        }

        public DbSet<Worker> Workers => Set<Worker>();
        public DbSet<Project> Projects => Set<Project>();
        public DbSet<TimesheetEntry> TimesheetEntries => Set<TimesheetEntry>();
        public DbSet<WorkerRate> WorkerRates => Set<WorkerRate>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<SupplierCost> SupplierCosts => Set<SupplierCost>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<ApplicationSchedule> ApplicationSchedules => Set<ApplicationSchedule>();
        public DbSet<TimesheetUser> TimesheetUsers => Set<TimesheetUser>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // 1) Prefer explicit env var to avoid accidental wrong DB.
                var cs = Environment.GetEnvironmentVariable("MEP1_ERP_DB");
                if (!string.IsNullOrWhiteSpace(cs))
                {
                    optionsBuilder.UseSqlite(cs);
                    return;
                }

                // 2) Default fallback (dev DB in /data)
                var baseDir = AppContext.BaseDirectory;
                var rootDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
                var dbPath = Path.Combine(rootDir, "data", "mep1_erp_dev.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Worker>()
                .HasIndex(w => w.Initials)
                .IsUnique();

            modelBuilder.Entity<Project>()
                .HasIndex(p => new { p.JobNameOrNumber, p.Company });

            modelBuilder.Entity<TimesheetEntry>()
                .HasIndex(e => new { e.WorkerId, e.EntryId })
                .IsUnique();

            modelBuilder.Entity<WorkerRate>()
                .HasIndex(r => new { r.WorkerId, r.ValidFrom });

            modelBuilder.Entity<Supplier>()
                .HasIndex(s => s.Name)
                .IsUnique();

            modelBuilder.Entity<SupplierCost>()
                .HasIndex(sc => new { sc.ProjectId, sc.SupplierId, sc.Date });

            modelBuilder.Entity<SupplierCost>()
                .HasOne(sc => sc.Project)
                .WithMany(p => p.SupplierCosts)
                .HasForeignKey(sc => sc.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SupplierCost>()
                .HasOne(sc => sc.Supplier)
                .WithMany(s => s.SupplierCosts)
                .HasForeignKey(sc => sc.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => i.InvoiceNumber);

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => i.ProjectCode);

            modelBuilder.Entity<ApplicationSchedule>()
                .HasIndex(a => new { a.ProjectCode, a.ApplicationSubmissionDate });

            modelBuilder.Entity<TimesheetUser>(e =>
            {
                e.ToTable("TimesheetUsers");
                e.HasKey(x => x.Id);

                e.Property(x => x.Username).IsRequired().HasMaxLength(64);
                e.HasIndex(x => x.Username).IsUnique();

                e.Property(x => x.PasswordHash).IsRequired().HasMaxLength(255);

                e.Property(x => x.IsActive).HasDefaultValue(true);

                e.Property(x => x.WorkerId).IsRequired();

                e.Property(x => x.Role)
                 .HasConversion<string>()
                 .HasMaxLength(16)
                 .HasDefaultValue(TimesheetUserRole.Worker);

                e.Property(x => x.MustChangePassword)
                 .HasDefaultValue(false);
            });

            modelBuilder.Entity<AuditLog>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.ActorRole).HasMaxLength(32);
                e.Property(x => x.ActorSource).HasMaxLength(32);

                e.Property(x => x.Action).IsRequired().HasMaxLength(128);
                e.Property(x => x.EntityType).IsRequired().HasMaxLength(64);
                e.Property(x => x.EntityId).IsRequired().HasMaxLength(64);
            });

        }
    }
}
