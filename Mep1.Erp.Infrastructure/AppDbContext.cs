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
        public DbSet<Application> Applications => Set<Application>();
        public DbSet<ApplicationSchedule> ApplicationSchedules => Set<ApplicationSchedule>();
        public DbSet<TimesheetUser> TimesheetUsers => Set<TimesheetUser>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Company> Companies => Set<Company>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<ProjectCcfRef> ProjectCcfRefs => Set<ProjectCcfRef>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured)
                return;

            // Prefer the same env vars as the API (so migrations/importer can target Postgres too)
            var provider = Environment.GetEnvironmentVariable("Database__Provider");
            var cs = Environment.GetEnvironmentVariable("ConnectionStrings__ErpDb");

            // Back-compat fallback for local/dev (existing env var you had)
            var legacySqlite = Environment.GetEnvironmentVariable("MEP1_ERP_DB");

            // If caller explicitly chose Postgres, use it.
            if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(cs))
                    throw new InvalidOperationException("ConnectionStrings__ErpDb is required when Database__Provider=Postgres.");

                optionsBuilder.UseNpgsql(cs);
                return;
            }

            // If caller explicitly chose Sqlite, use it.
            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(cs))
                {
                    optionsBuilder.UseSqlite(cs);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(legacySqlite))
                {
                    optionsBuilder.UseSqlite(legacySqlite);
                    return;
                }
            }

            // If provider wasn't specified, infer from connection string format.
            if (!string.IsNullOrWhiteSpace(cs))
            {
                // Very simple inference: Npgsql connection strings usually contain "Host="
                if (cs.Contains("Host=", StringComparison.OrdinalIgnoreCase))
                {
                    optionsBuilder.UseNpgsql(cs);
                    return;
                }

                // Otherwise assume it's a sqlite connection string (e.g. "Data Source=...")
                optionsBuilder.UseSqlite(cs);
                return;
            }

            // Final fallback: dev SQLite file in /data
            var baseDir = AppContext.BaseDirectory;
            var rootDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            var dbPath = Path.Combine(rootDir, "data", "mep1_erp_dev.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var isSqlite = Database.ProviderName != null &&
              Database.ProviderName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Worker>()
                .HasIndex(w => w.Initials)
                .IsUnique();

            modelBuilder.Entity<Project>()
                .HasIndex(p => new { p.JobNameOrNumber, p.CompanyId });

            modelBuilder.Entity<Project>()
                .HasOne(p => p.CompanyEntity)
                .WithMany(c => c.Projects)
                .HasForeignKey(p => p.CompanyId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Company>()
                .HasIndex(c => c.Code)
                .IsUnique();

            modelBuilder.Entity<Company>()
                .Property(c => c.Code)
                .IsRequired();

            modelBuilder.Entity<TimesheetEntry>()
                .HasIndex(e => new { e.WorkerId, e.EntryId })
                .IsUnique();

            modelBuilder.Entity<WorkerRate>()
                .HasIndex(r => new { r.WorkerId, r.ValidFrom });

            // Postgres-safe: these are effective dates, not instants in time
            modelBuilder.Entity<WorkerRate>(e =>
            {
                e.Property(x => x.ValidFrom).HasColumnType("date");
                e.Property(x => x.ValidTo).HasColumnType("date");
            });

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

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => i.InvoiceNumber);

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => i.ProjectCode);

            // Applications (real submissions) - distinct from ApplicationSchedule (notifications/rules)
            modelBuilder.Entity<Application>(e =>
            {
                e.HasIndex(a => new { a.ProjectCode, a.ApplicationNumber })
                 .IsUnique();

                e.HasIndex(a => a.ProjectCode);

                // Postgres-safe: these are business "dates", not instants in time
                if (!isSqlite)
                {
                    e.Property(x => x.DateApplied).HasColumnType("date");
                    e.Property(x => x.DateAgreed).HasColumnType("date");
                }

                e.Property(x => x.ProjectCode).IsRequired();

                e.Property(x => x.Status)
                 .IsRequired()
                 .HasMaxLength(32);
            });

            // Optional 1:1 link: Invoice chooses an Application
            // UNIQUE on nullable ApplicationId enforces 1 invoice per application while allowing many nulls in Postgres.
            modelBuilder.Entity<Invoice>()
                .HasIndex(i => i.ApplicationId)
                .IsUnique();

            modelBuilder.Entity<Invoice>()
                .HasOne(i => i.Application)
                .WithOne(a => a.Invoice)
                .HasForeignKey<Invoice>(i => i.ApplicationId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

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

                e.Property(x => x.UsernameNormalized)
                    .IsRequired()
                    .HasMaxLength(200);

                e.HasIndex(x => x.UsernameNormalized)
                    .IsUnique();
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

            modelBuilder.Entity<RefreshToken>(e =>
            {
                e.ToTable("RefreshTokens");
                e.HasKey(x => x.Id);

                e.Property(x => x.TokenHash).IsRequired().HasMaxLength(255);
                e.HasIndex(x => x.TokenHash).IsUnique();

                e.HasOne(x => x.TimesheetUser)
                    .WithMany() // or .WithMany(u => u.RefreshTokens) if you add nav prop
                    .HasForeignKey(x => x.TimesheetUserId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.CreatedUtc).IsRequired();
                e.Property(x => x.ExpiresUtc).IsRequired();
            });

            modelBuilder.Entity<ProjectCcfRef>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.Code)
                    .IsRequired()
                    .HasMaxLength(3);

                e.HasIndex(x => new { x.ProjectId, x.Code })
                    .IsUnique();

                e.HasOne(x => x.Project)
                    .WithMany() // (optional) you can add Project.ProjectCcfRefs later if you want
                    .HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                e.Property(x => x.EstimatedValue)
                    .HasColumnType("decimal(18,2)");

                e.Property(x => x.QuotedValue)
                    .HasColumnType("decimal(18,2)");

                e.Property(x => x.AgreedValue)
                    .HasColumnType("decimal(18,2)");

                e.Property(x => x.ActualValue)
                    .HasColumnType("decimal(18,2)");

                e.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(32);

                e.Property(x => x.Notes)
                    .HasMaxLength(1000);

                e.Property(x => x.IsDeleted)
                    .HasDefaultValue(false);

                e.Property(x => x.DeletedAtUtc);

                e.Property(x => x.DeletedByWorkerId);

            });

            modelBuilder.Entity<TimesheetEntry>()
                .HasOne(e => e.ProjectCcfRef)
                .WithMany()
                .HasForeignKey(e => e.ProjectCcfRefId)
                .OnDelete(DeleteBehavior.Restrict);
        }

        private static string NormalizeUsername(string? username)
            => (username ?? "").Trim().ToLowerInvariant();

        private void ApplyTimesheetUserNormalization()
        {
            foreach (var entry in ChangeTracker.Entries<TimesheetUser>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    // Only touch it if Username is present
                    var username = entry.Entity.Username;
                    entry.Entity.UsernameNormalized = NormalizeUsername(username);
                }
            }
        }

        public override int SaveChanges()
        {
            ApplyTimesheetUserNormalization();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyTimesheetUserNormalization();
            return base.SaveChangesAsync(cancellationToken);
        }

    }
}
