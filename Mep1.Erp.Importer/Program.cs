using ClosedXML.Excel;
using DocumentFormat.OpenXml.Math;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace Mep1.Erp.Importer
{
    internal class Program
    {
        static string GetConfigPath()
        {
            return AppSettingsHelper.GetConfigPath();
        }

        static string? GetEnv(string key)
    => Environment.GetEnvironmentVariable(key);

        static (string provider, string connectionString) GetDbTarget()
        {
            // Prefer API-style env vars (works great for VPS + SSH tunnel)
            var providerEnv =
                GetEnv("Database__Provider") ??
                GetEnv("MEP1_ERP_DB_PROVIDER"); // optional extra alias

            var csEnv =
                GetEnv("ConnectionStrings__ErpDb") ??
                GetEnv("MEP1_ERP_DB_CONNECTION"); // optional extra alias

            if (!string.IsNullOrWhiteSpace(providerEnv) && !string.IsNullOrWhiteSpace(csEnv))
            {
                return (providerEnv.Trim(), csEnv.Trim());
            }

            // Fallback: your existing local settings.json workflow
            var cs = GetErpDbConnectionStringFromConfig();
            var inferredProvider = InferProviderFromConnectionString(cs);
            return (inferredProvider, cs);
        }

        static string InferProviderFromConnectionString(string cs)
        {
            var s = (cs ?? "").Trim();

            // Quick heuristic:
            // - SQLite often starts with "Data Source="
            // - Postgres commonly contains "Host=" or "Username="
            if (s.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ||
                s.Contains(".db", StringComparison.OrdinalIgnoreCase))
                return "Sqlite";

            if (s.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Username=", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Port=", StringComparison.OrdinalIgnoreCase))
                return "Postgres";

            // Default to Sqlite for safety (matches your current dev world)
            return "Sqlite";
        }

        static DbContextOptions<AppDbContext> BuildDbOptions(string provider, string connectionString)
        {
            var p = (provider ?? "").Trim().ToLowerInvariant();

            var builder = new DbContextOptionsBuilder<AppDbContext>();

            if (p == "postgres" || p == "postgresql" || p == "npgsql")
            {
                builder.UseNpgsql(connectionString);
                return builder.Options;
            }

            // Default: Sqlite
            builder.UseSqlite(connectionString);
            return builder.Options;
        }

        static string GetErpDbConnectionStringFromConfig()
        {
            var configPath = GetConfigPath();
            AppSettings settings;

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            if (!string.IsNullOrWhiteSpace(settings.ErpDbConnectionString))
                return settings.ErpDbConnectionString;

            Console.WriteLine("ERP DB connection string is not configured.");
            Console.WriteLine("Paste the DB connection string (SQLite example: Data Source=../data/mep1_erp_dev.db)");
            Console.WriteLine("Postgres example: Host=localhost;Port=5433;Database=mep1_erp;Username=mep1;Password=...;");
            var input = Console.ReadLine()!.Trim('"', ' ');

            while (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("That value was empty. Please try again:");
                input = Console.ReadLine()!.Trim('"', ' ');
            }

            settings.ErpDbConnectionString = input;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, options));

            Console.WriteLine($"Saved settings to {configPath}");
            return input;
        }

        static string GetTimesheetFolderFromConfig()
        {
            var configPath = GetConfigPath();
            AppSettings settings;

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            if (!string.IsNullOrWhiteSpace(settings.TimesheetFolder) &&
                Directory.Exists(settings.TimesheetFolder))
            {
                return settings.TimesheetFolder;
            }

            // No valid config – ask user (this will very rarely run once Settings UI is in use)
            Console.WriteLine("Timesheet folder is not configured.");
            Console.WriteLine("Please paste or type the full path to the '07. Timesheets' folder:");
            var input = Console.ReadLine()!.Trim('"', ' ');

            while (!Directory.Exists(input))
            {
                Console.WriteLine("That path does not exist. Please try again:");
                input = Console.ReadLine()!.Trim('"', ' ');
            }

            settings.TimesheetFolder = input;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, options));

            Console.WriteLine($"Saved settings to {configPath}");
            return input;
        }

        static string GetFinanceSheetPathFromConfig()
        {
            var configPath = GetConfigPath();
            AppSettings settings;

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            if (!string.IsNullOrWhiteSpace(settings.FinanceSheetPath) &&
                File.Exists(settings.FinanceSheetPath))
            {
                return settings.FinanceSheetPath;
            }

            // Ask user
            Console.WriteLine("Finance Sheet path is not configured.");
            Console.WriteLine("Please paste or type the full path to the 'Finance Sheet' workbook:");
            var input = Console.ReadLine()!.Trim('"', ' ');

            while (!File.Exists(input))
            {
                Console.WriteLine("That file does not exist. Please try again:");
                input = Console.ReadLine()!.Trim('"', ' ');
            }

            settings.FinanceSheetPath = input;

            // Also keep existing TimesheetFolder if we have it
            if (string.IsNullOrWhiteSpace(settings.TimesheetFolder))
            {
                Console.WriteLine("If you haven't already configured the timesheet folder, you'll be asked on next run.");
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, options));

            Console.WriteLine($"Saved settings to {configPath}");
            return input;
        }

        static string GetJobSourcePathFromConfig()
        {
            var configPath = GetConfigPath();
            AppSettings settings;

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            if (!string.IsNullOrWhiteSpace(settings.JobSourcePath) &&
                File.Exists(settings.JobSourcePath))
            {
                return settings.JobSourcePath;
            }

            Console.WriteLine("Job Source workbook path is not configured.");
            Console.WriteLine("Please paste or type the full path to 'Job Source Info.xlsm':");
            var input = Console.ReadLine()!.Trim('"', ' ');

            while (!File.Exists(input))
            {
                Console.WriteLine("That file does not exist. Please try again:");
                input = Console.ReadLine()!.Trim('"', ' ');
            }

            settings.JobSourcePath = input;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, options));

            Console.WriteLine($"Saved settings to {configPath}");
            return input;
        }

        static string GetInvoiceRegisterPathFromConfig()
        {
            var configPath = GetConfigPath();
            AppSettings settings;

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            if (!string.IsNullOrWhiteSpace(settings.InvoiceRegisterPath) &&
                File.Exists(settings.InvoiceRegisterPath))
            {
                return settings.InvoiceRegisterPath;
            }

            Console.WriteLine("Invoice Register workbook path is not configured.");
            Console.WriteLine("Please paste or type the full path to 'Invoice Register.xlsx':");
            var input = Console.ReadLine()!.Trim('"', ' ');

            while (!File.Exists(input))
            {
                Console.WriteLine("That file does not exist. Please try again:");
                input = Console.ReadLine()!.Trim('"', ' ');
            }

            settings.InvoiceRegisterPath = input;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, options));

            Console.WriteLine($"Saved settings to {configPath}");
            return input;
        }

        static string GetApplicationSchedulePathFromConfig()
        {
            var configPath = GetConfigPath();
            AppSettings settings;

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            if (!string.IsNullOrWhiteSpace(settings.ApplicationSchedulePath) &&
                File.Exists(settings.ApplicationSchedulePath))
            {
                return settings.ApplicationSchedulePath;
            }

            Console.WriteLine("Application Schedule workbook path is not configured.");
            Console.WriteLine("Please paste or type the full path to 'Application Schedule.xlsx':");
            var input = Console.ReadLine()!.Trim('"', ' ');

            while (!File.Exists(input))
            {
                Console.WriteLine("That file does not exist. Please try again:");
                input = Console.ReadLine()!.Trim('"', ' ');
            }

            settings.ApplicationSchedulePath = input;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, options));

            Console.WriteLine($"Saved settings to {configPath}");
            return input;
        }

        static void Main(string[] args)
        {
            var timesheetFolder = GetTimesheetFolderFromConfig();

            if (!Directory.Exists(timesheetFolder))
            {
                Console.WriteLine("Timesheet folder not found:");
                Console.WriteLine(timesheetFolder);
                Console.WriteLine("Press Enter to exit.");
                Console.ReadLine();
                return;
            }

            var files = Directory.GetFiles(
                timesheetFolder,
                "MEP1 BIM Technical Diary (*) New.xlsm");

            Console.WriteLine("Looking in: " + timesheetFolder);

            var filesTwo = Directory.GetFiles(timesheetFolder, "MEP1 BIM Technical Diary (*) New.xlsm");

            Console.WriteLine($"Found {filesTwo.Length} timesheet files:");
            foreach (var f in filesTwo)
            {
                Console.WriteLine("  " + Path.GetFileName(f));
            }

            Console.WriteLine($"Found {files.Length} timesheet files.");

            var (provider, erpDbCs) = GetDbTarget();

            Console.WriteLine($"DB Provider: {provider}");
            Console.WriteLine($"DB Connection: {MaskConnectionStringForLog(provider, erpDbCs)}");

            var dbOptions = BuildDbOptions(provider, erpDbCs);
            using var db = new AppDbContext(dbOptions);

            // Use migrations now (not EnsureCreated)
            db.Database.Migrate();

            // Bootstrap: ensure at least one TimesheetUser exists on a fresh database
            if (!db.TimesheetUsers.Any())
            {
                const string username = "jason.dean";
                const string tempPassword = "test1234";

                // Find-or-create the owner worker (Jason Dean) by a stable identifier.
                // Do NOT assume WorkerId == 1, because IDs depend on insert order.
                var ownerWorker = db.Workers.SingleOrDefault(w => w.Name == "Jason Dean");
                if (ownerWorker == null)
                {
                    ownerWorker = new Worker
                    {
                        Name = "Jason Dean",
                        Initials = "JWD",
                        IsActive = true
                    };

                    db.Workers.Add(ownerWorker);
                    db.SaveChanges();
                }

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                db.TimesheetUsers.Add(new TimesheetUser
                {
                    Username = username,
                    PasswordHash = passwordHash,
                    WorkerId = ownerWorker.Id,
                    Role = TimesheetUserRole.Owner,
                    MustChangePassword = true,
                    IsActive = true
                });

                db.SaveChanges();

                Console.WriteLine("Seeded initial OWNER TimesheetUser:");
                Console.WriteLine("  Username: Jason Dean");
                Console.WriteLine("  Temp password: ChangeMe123!");
                Console.WriteLine("  MustChangePassword = true");
                Console.WriteLine($"  Linked worker: {ownerWorker.Name} (Id={ownerWorker.Id})");
            }

            // Import worker rate history from Finance Sheet
            var financeSheetPath = GetFinanceSheetPathFromConfig();
            ImportWorkerRates(db, financeSheetPath);

            // Import projects from Job Source Info
            var jobSourcePath = GetJobSourcePathFromConfig();
            ImportProjectsFromJobSource(db, jobSourcePath);

            // Only used once on a one-off, might as well be deleted.
            // BackfillProjectClassification(db);

            // Import invoices from Invoice Register
            var invoiceRegisterPath = GetInvoiceRegisterPathFromConfig();
            ImportInvoices(db, invoiceRegisterPath);

            // Import application schedule
            var appSchedulePath = GetApplicationSchedulePathFromConfig();
            ImportApplicationSchedule(db, appSchedulePath);

            var allImported = new List<TimesheetEntry>();

            foreach (var file in files)
            {
                Console.WriteLine($"Importing {Path.GetFileName(file)}...");
                var entries = ImportTimesheetFile(db, file);
                allImported.AddRange(entries);
            }

            Console.WriteLine();
            Console.WriteLine($"New or updated TIMESHEET entries this run: {allImported.Count}.");

            var dbTotal = db.TimesheetEntries.Count();
            Console.WriteLine($"Database currently contains {dbTotal} timesheet entries.");

            if (allImported.Count == 0)
            {
                Console.WriteLine("No timesheet changes detected.");
            }
            else
            {
                Console.WriteLine("Timesheet changes have been applied to the database this run.");
            }

            // (Optionally, log that support tables were refreshed)
            Console.WriteLine("Worker rate history refreshed from Finance Sheet.");
            Console.WriteLine("Projects synchronised from Job Source Info.");

            // Previous full month
            var now = DateTime.Now;
            var reportMonthDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1);

            PrintWorkerHoursAndCostForMonth(db, reportMonthDate.Year, reportMonthDate.Month);

            PrintProjectCostVsInvoiced(db);

            Console.WriteLine();
            Console.WriteLine("Done. Press Enter to exit.");
            Console.ReadLine();
        }

        private static string NormalizeCcfRef(string input)
        {
            var raw = (input ?? "").Trim();

            if (raw.Length == 0)
                throw new InvalidOperationException("CCF Ref is required.");

            for (int i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (ch < '0' || ch > '9')
                    throw new InvalidOperationException("CCF Ref must be numeric.");
            }

            if (!int.TryParse(raw, out var n))
                throw new InvalidOperationException("Invalid CCF Ref.");

            if (n == 0)
                throw new InvalidOperationException("CCF Ref 000 is not allowed.");

            if (n < 1 || n > 999)
                throw new InvalidOperationException("CCF Ref must be between 001 and 999.");

            return n.ToString("D3");
        }

        private static int EnsureProjectCcfRefId(AppDbContext db, int projectId, string ccfText)
        {
            var normalized = NormalizeCcfRef(ccfText);

            var existing = db.ProjectCcfRefs
                .SingleOrDefault(x => x.ProjectId == projectId && x.Code == normalized);

            if (existing != null)
            {
                if (!existing.IsActive)
                    throw new InvalidOperationException("CCF Ref is inactive.");
                return existing.Id;
            }

            var created = new ProjectCcfRef
            {
                ProjectId = projectId,
                Code = normalized,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.ProjectCcfRefs.Add(created);
            db.SaveChanges(); // need Id immediately

            return created.Id;
        }

        private static List<TimesheetEntry> ImportTimesheetFile(AppDbContext db, string path)
        {
            var result = new List<TimesheetEntry>();

            int createdCount = 0;
            int updatedCount = 0;

            var initials = GetInitialsFromFilename(path);
            if (string.IsNullOrWhiteSpace(initials))
            {
                Console.WriteLine($"  Could not extract initials from filename: {path}");
                return result;
            }

            // Ensure worker exists
            var worker = db.Workers.SingleOrDefault(w => w.Initials == initials);
            if (worker == null)
            {
                worker = new Worker
                {
                    Initials = initials,
                    Name = initials
                };
                db.Workers.Add(worker);
                db.SaveChanges();
            }

            // NEW: cache existing entries for this worker by EntryId
            var existingEntries = db.TimesheetEntries
                .Where(e => e.WorkerId == worker.Id)
                .ToDictionary(e => e.EntryId, e => e);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("All Data Entries"); // important change here

            // Figure out data range
            var firstUsedRow = sheet.FirstRowUsed().RowNumber();   // header row
            var firstDataRow = firstUsedRow + 1;
            var lastDataRow = sheet.LastRowUsed().RowNumber();

            int debugRowsPrinted = 0;

            for (int row = firstDataRow; row <= lastDataRow; row++)
            {
                var dateCell = sheet.Cell(row, 1);
                var hoursCell = sheet.Cell(row, 3);
                var companyCell = sheet.Cell(row, 4);
                var codeCell = sheet.Cell(row, 5);
                var jobNameCell = sheet.Cell(row, 6);
                var taskCell = sheet.Cell(row, 7);
                var ccfCell = sheet.Cell(row, 8);
                var entryIdCell = sheet.Cell(row, 9); // Column I: EntryId

                // --- NEW: read EntryId ---
                if (!entryIdCell.TryGetValue<int>(out int entryId) || entryId <= 0)
                {
                    // No valid EntryId → skip this row (shouldn't happen now that you've backfilled)
                    continue;
                }

                // Accept empty or zero hours rows (e.g. holiday, sick, admin)
                decimal hours = 0;

                var hoursText = hoursCell.GetValue<string>().Trim();

                // Try to parse hours if any text exists
                if (!string.IsNullOrWhiteSpace(hoursText))
                {
                    decimal.TryParse(hoursText, NumberStyles.Any, CultureInfo.InvariantCulture, out hours);
                    decimal.TryParse(hoursText, NumberStyles.Any, CultureInfo.CurrentCulture, out hours);
                }
                // hours can now be 0 and still be a valid entry

                DateTime date;

                if (!dateCell.TryGetValue<DateTime>(out date))
                {
                    var dateText = dateCell.GetValue<string>().Trim();
                    if (!DateTime.TryParse(dateText, out date))
                        continue;
                }

                date = AsUtcDate(date); // Normalize

                var company = companyCell.GetString().Trim();
                var code = codeCell.GetString().Trim();
                var jobName = jobNameCell.GetString().Trim();

                // Debug: show first few rows we think are valid
                if (debugRowsPrinted < 5)
                {
                    Console.WriteLine($"  Row {row}: {date:yyyy-MM-dd}, hours={hours}, code='{code}', job='{jobName}'");
                    debugRowsPrinted++;
                }

                // --- Find project by Job Name/No. ---
                // Job Source Info is the *only* source of truth for projects.
                var project = db.Projects
                    .SingleOrDefault(p => p.JobNameOrNumber == jobName);

                if (project == null)
                {
                    // Do NOT create a project here – we only trust Job Source Info.
                    Console.WriteLine(
                        $"  WARNING: Job '{jobName}' (company '{company}') not found in Job Source Info. " +
                        "Skipping this timesheet row.");
                    continue;
                }

                var taskText = taskCell.GetString();
                var ccfText = ccfCell.GetString();

                int? projectCcfRefId = null;

                if (string.Equals(code, "VO", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        projectCcfRefId = EnsureProjectCcfRefId(db, project.Id, ccfText);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"  WARNING: Invalid CCF Ref '{ccfText}' for VO on job '{jobName}' (row {row}). {ex.Message} Skipping row.");
                        continue;
                    }
                }

                // --- Upsert by (WorkerId, EntryId), including Code and Project ---
                if (existingEntries.TryGetValue(entryId, out var existing))
                {
                    bool changed = false;

                    if (existing.Date != date)
                    {
                        existing.Date = date;
                        changed = true;
                    }
                    if (existing.Hours != hours)
                    {
                        existing.Hours = hours;
                        changed = true;
                    }
                    if (!string.Equals(existing.Code, code, StringComparison.Ordinal))
                    {
                        existing.Code = code;
                        changed = true;
                    }
                    if (!string.Equals(existing.TaskDescription, taskText, StringComparison.Ordinal))
                    {
                        existing.TaskDescription = taskText;
                        changed = true;
                    }
                    // CCF is only stored for VO; otherwise must be null
                    if (existing.ProjectCcfRefId != projectCcfRefId)
                    {
                        existing.ProjectCcfRefId = projectCcfRefId;
                        changed = true;
                    }
                    if (existing.ProjectId != project.Id)
                    {
                        existing.ProjectId = project.Id;
                        changed = true;
                    }

                    if (changed)
                    {
                        updatedCount++;
                        result.Add(existing);
                    }
                }
                else
                {
                    var entry = new TimesheetEntry
                    {
                        EntryId = entryId,
                        Date = date,
                        Hours = hours,
                        Code = code,
                        TaskDescription = taskText,
                        ProjectCcfRefId = projectCcfRefId,
                        WorkerId = worker.Id,
                        ProjectId = project.Id
                    };

                    db.TimesheetEntries.Add(entry);
                    existingEntries[entryId] = entry;

                    createdCount++;
                    result.Add(entry);
                }
            }

            db.SaveChanges();
            Console.WriteLine($"  New: {createdCount}, Updated: {updatedCount} (file total: {createdCount + updatedCount})");
            return result;
        }

        private static void ImportWorkerRates(AppDbContext db, string financeSheetPath)
        {
            Console.WriteLine();
            Console.WriteLine("Importing worker rate history from Finance Sheet...");

            using var workbook = new XLWorkbook(financeSheetPath);
            var sheet = workbook.Worksheet("Information");

            var firstRow = sheet.FirstRowUsed().RowNumber();
            var headerRow = firstRow;
            var firstDataRow = headerRow + 1;
            var lastDataRow = sheet.LastRowUsed().RowNumber();

            // For now, rebuild the whole rate table from the Finance Sheet
            db.WorkerRates.RemoveRange(db.WorkerRates);
            db.SaveChanges();

            int created = 0;

            for (int row = firstDataRow; row <= lastDataRow; row++)
            {
                var initials = sheet.Cell(row, 1).GetString().Trim(); // A: Initials
                if (string.IsNullOrWhiteSpace(initials))
                    continue;

                // Collect (effectiveDate, rate) points from the row
                var points = new List<(DateTime date, decimal rate)>();

                void TryAddRate(int rateCol, int dateCol)
                {
                    var rateCell = sheet.Cell(row, rateCol);
                    var dateCell = sheet.Cell(row, dateCol);

                    var rateText = rateCell.GetString().Trim();
                    var dateText = dateCell.GetString().Trim();

                    if (string.IsNullOrWhiteSpace(rateText) || string.IsNullOrWhiteSpace(dateText))
                        return;

                    // parse rate (try invariant then current culture)
                    decimal rate;
                    if (!decimal.TryParse(rateText, NumberStyles.Any, CultureInfo.InvariantCulture, out rate) &&
                        !decimal.TryParse(rateText, NumberStyles.Any, CultureInfo.CurrentCulture, out rate))
                    {
                        return;
                    }

                    // parse date
                    DateTime dt;
                    if (!dateCell.TryGetValue<DateTime>(out dt) && !DateTime.TryParse(dateText, out dt))
                    {
                        return;
                    }

                    dt = AsUtcDate(dt); // Normalize

                    points.Add((dt, rate));
                }

                // Current rate: C/D = 3/4
                TryAddRate(3, 4);

                // Previous rate pairs: (E/F = 5/6), (G/H = 7/8), ... up to 10 previous
                for (int pair = 0; pair < 10; pair++)
                {
                    int rateCol = 5 + pair * 2;  // E is 5
                    int dateCol = 6 + pair * 2;  // F is 6
                    TryAddRate(rateCol, dateCol);
                }

                if (points.Count == 0)
                    continue;

                // Ensure worker exists (reuse initials, use Name from column B if present)
                var worker = db.Workers.SingleOrDefault(w => w.Initials == initials);
                if (worker == null)
                {
                    var name = sheet.Cell(row, 2).GetString().Trim(); // B: Name
                    worker = new Worker
                    {
                        Initials = initials,
                        Name = string.IsNullOrWhiteSpace(name) ? initials : name
                    };
                    db.Workers.Add(worker);
                    db.SaveChanges();
                }

                // Sort by effective date ascending
                points.Sort((a, b) => a.date.CompareTo(b.date));

                // Create rate periods: [ValidFrom, ValidTo) ranges
                for (int i = 0; i < points.Count; i++)
                {
                    var from = AsUtcDate(points[i].date);
                    DateTime? to = null;

                    if (i < points.Count - 1)
                        to = AsUtcDate(points[i + 1].date);

                    var wr = new WorkerRate
                    {
                        WorkerId = worker.Id,
                        RatePerHour = points[i].rate,
                        ValidFrom = from,
                        ValidTo = to
                    };

                    db.WorkerRates.Add(wr);
                    created++;
                }
            }

            db.SaveChanges();
            Console.WriteLine("  Created " + created + " worker rate records.");
        }

        static readonly HashSet<string> NonCompanyCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "HOL",
            "SI",
            "TP",
            "MEP1"
        };

        private static void ImportProjectsFromJobSource(AppDbContext db, string jobSourcePath)
        {
            Console.WriteLine();
            Console.WriteLine("Importing projects from Job Source Info...");

            using var workbook = new XLWorkbook(jobSourcePath);
            var sheet = workbook.Worksheet(1); // first sheet (Job Name/No, Company)

            var firstRow = sheet.FirstRowUsed().RowNumber();
            var headerRow = firstRow;
            var firstDataRow = headerRow + 1;
            var lastDataRow = sheet.LastRowUsed().RowNumber();

            // 🔵 Incremental sync:
            // - Do NOT delete Projects.
            // - For each job in the sheet:
            //      if it exists: update Company if needed
            //      if not:       create new Project

            // Cache all existing projects by JobNameOrNumber
            var existing = db.Projects
                .ToDictionary(p => p.JobNameOrNumber, p => p);

            int created = 0;
            int updated = 0;

            // Cache existing companies by Code (normalized)
            var companiesByCode = db.Companies
                .AsNoTracking()
                .ToDictionary(c => (c.Code ?? "").Trim().ToUpperInvariant(), c => c);

            for (int row = firstDataRow; row <= lastDataRow; row++)
            {
                var jobName = sheet.Cell(row, 1).GetString().Trim(); // A: Job Name/No.

                if (string.IsNullOrWhiteSpace(jobName))
                    continue;

                var companyRaw = sheet.Cell(row, 2).GetString().Trim();
                var companyCode = (companyRaw ?? "").Trim().ToUpperInvariant();

                int? companyId = null;

                // Only create/link a real Company when it’s not one of the legacy pseudo-codes
                if (!string.IsNullOrWhiteSpace(companyCode) && !NonCompanyCodes.Contains(companyCode))
                {
                    companyId = EnsureCompanyId(db, companiesByCode, companyCode);
                }

                if (existing.TryGetValue(jobName, out var proj))
                {
                    //// Keep legacy string in sync (optional but useful for now)
                    //if (!string.Equals((proj.Company ?? "").Trim(), companyCode, StringComparison.Ordinal))
                    //{
                    //    proj.Company = companyCode;
                    //    updated++;
                    //}

                    if (proj.CompanyId != companyId)
                    {
                        proj.CompanyId = companyId;
                        updated++;
                    }
                }
                else
                {
                    var (category, isReal) = ClassifyJobName(jobName);

                    var project = new Project
                    {
                        JobNameOrNumber = jobName,

                        CompanyId = companyId,      // nullable FK

                        Category = category,
                        IsRealProject = isReal,
                        IsActive = true
                    };

                    db.Projects.Add(project);
                    existing[jobName] = project;
                    created++;
                }
            }

            db.SaveChanges();
            Console.WriteLine($"  Projects: {created} created, {updated} updated (no deletes).");
        }

        private static void ImportInvoices(AppDbContext db, string registerPath)
        {
            Console.WriteLine();
            Console.WriteLine("Importing invoices from Invoice Register...");

            using var workbook = new XLWorkbook(registerPath);
            var sheet = workbook.Worksheet("Register"); // sheet name you mentioned

            // Header is on row 3
            var headerRow = 3;
            var lastColumn = sheet.Row(headerRow).LastCellUsed().Address.ColumnNumber;

            // Build a map: header text -> column index
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= lastColumn; col++)
            {
                var header = sheet.Cell(headerRow, col).GetString().Trim();
                if (!string.IsNullOrEmpty(header) && !columns.ContainsKey(header))
                {
                    columns[header] = col;
                }
            }

            // Helper local functions to read values by header name
            string GetString(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return "";
                return row.Cell(col).GetString().Trim();
            }

            DateTime? GetDate(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return null;
                var cell = row.Cell(col);

                DateTime dt;

                // ClosedXML often gives DateTime.Kind = Unspecified
                if (cell.TryGetValue<DateTime>(out dt))
                    return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);

                var txt = cell.GetString().Trim();
                if (DateTime.TryParse(txt, out dt))
                    return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);

                return null;
            }

            int? GetInt(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return null;
                var cell = row.Cell(col);
                if (cell.TryGetValue<int>(out var i)) return i;

                var txt = cell.GetString().Trim();
                if (int.TryParse(txt, out i)) return i;

                return null;
            }

            decimal GetDecimalOrZero(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return 0m;
                var cell = row.Cell(col);

                if (cell.TryGetValue<decimal>(out var d)) return d;

                var txt = cell.GetString().Trim();
                if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
                if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.CurrentCulture, out d)) return d;

                return 0m;
            }

            decimal? GetDecimalNullable(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return null;
                var cell = row.Cell(col);

                if (cell.TryGetValue<decimal>(out var d)) return d;

                var txt = cell.GetString().Trim();
                if (string.IsNullOrEmpty(txt)) return null;

                if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
                if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.CurrentCulture, out d)) return d;

                return null;
            }

            // 🔵 NEW: try to detect the Excel table so we can ignore the totals row
            int firstDataRow = headerRow + 1;
            int lastDataRow;

            var invoiceTable = sheet.Tables
                .FirstOrDefault(t =>
                    t.ShowHeaderRow &&
                    string.Equals(t.Field(0).Name, "InvoiceNo", StringComparison.OrdinalIgnoreCase));

            if (invoiceTable != null)
            {
                // DataRange excludes the Totals row even if ShowTotalsRow == true
                lastDataRow = invoiceTable.DataRange.LastRow().RowNumber();
            }
            else
            {
                // Fallback: old behaviour
                lastDataRow = sheet.LastRowUsed().RowNumber();
            }

            // Cache existing invoices by a composite key so duplicate InvoiceNumbers are allowed
            var existing = db.Invoices
                .AsEnumerable()
                .ToDictionary(
                    i => GetInvoiceKey(i.InvoiceNumber, i.ProjectCode, i.InvoiceDate, i.NetAmount),
                    i => i);

            int created = 0;
            int updated = 0;

            for (int r = firstDataRow; r <= lastDataRow; r++)
            {
                var row = sheet.Row(r);

                var invoiceNoRaw = GetString(row, "InvoiceNo");
                var invoiceNo = NormalizeInvoiceNumber(invoiceNoRaw);

                if (!string.Equals(invoiceNoRaw, invoiceNo, StringComparison.Ordinal))
                {
                    Console.WriteLine($"  Normalized invoice number '{invoiceNoRaw}' → '{invoiceNo}'");
                }

                if (string.IsNullOrWhiteSpace(invoiceNo))
                    continue; // skip blanks

                // (optional extra safety – in case the totals label is something weird)
                if (invoiceNo.Equals("Total", StringComparison.OrdinalIgnoreCase) ||
                    invoiceNo.Equals("Totals", StringComparison.OrdinalIgnoreCase))
                    continue;

                var jobNo = GetString(row, "JobNo");
                var jobName = GetString(row, "JobName");
                var clientName = GetString(row, "ClientName");

                var invoiceDate = GetDate(row, "InvoiceDate") ?? DateTime.MinValue;
                var paymentTermsDays = GetInt(row, "PaymentTermsDays");
                var dueDate = GetDate(row, "DueDate");

                var netAmount = GetDecimalOrZero(row, "NetAmount");
                var vatRate = GetDecimalNullable(row, "VATRate");
                var vatAmount = GetDecimalNullable(row, "VATAmount");
                var grossAmount = GetDecimalNullable(row, "GrossAmount");
                var paymentAmount = GetDecimalNullable(row, "PaymentAmount");
                var paidDate = GetDate(row, "DatePaid");

                var status = GetString(row, "Status");
                var filePath = GetString(row, "FilePath");
                var notes = GetString(row, "Notes");

                var key = GetInvoiceKey(invoiceNo, jobNo, invoiceDate, netAmount);

                // Simple derived IsPaid flag
                bool isPaid = false;
                if (paymentAmount.HasValue && paymentAmount.Value > 0)
                {
                    isPaid = true;
                }
                else if (!string.IsNullOrEmpty(status) &&
                         status.IndexOf("paid", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isPaid = true;
                }

                if (existing.TryGetValue(key, out var invoice))
                {
                    // Update existing invoice if anything changed
                    bool changed = false;

                    if (invoice.ProjectCode != jobNo)
                    {
                        invoice.ProjectCode = jobNo;
                        changed = true;
                    }

                    if (invoice.JobName != jobName)
                    {
                        invoice.JobName = jobName;
                        changed = true;
                    }

                    if (invoice.ClientName != clientName)
                    {
                        invoice.ClientName = clientName;
                        changed = true;
                    }

                    if (invoice.InvoiceDate != invoiceDate)
                    {
                        invoice.InvoiceDate = invoiceDate;
                        changed = true;
                    }

                    if (invoice.PaymentTermsDays != paymentTermsDays)
                    {
                        invoice.PaymentTermsDays = paymentTermsDays;
                        changed = true;
                    }

                    if (invoice.DueDate != dueDate)
                    {
                        invoice.DueDate = dueDate;
                        changed = true;
                    }

                    if (invoice.NetAmount != netAmount)
                    {
                        invoice.NetAmount = netAmount;
                        changed = true;
                    }

                    if (invoice.VatRate != vatRate)
                    {
                        invoice.VatRate = vatRate;
                        changed = true;
                    }

                    if (invoice.VatAmount != vatAmount)
                    {
                        invoice.VatAmount = vatAmount;
                        changed = true;
                    }

                    if (invoice.GrossAmount != grossAmount)
                    {
                        invoice.GrossAmount = grossAmount;
                        changed = true;
                    }

                    if (invoice.PaymentAmount != paymentAmount)
                    {
                        invoice.PaymentAmount = paymentAmount;
                        changed = true;
                    }

                    if (invoice.PaidDate != paidDate)
                    {
                        invoice.PaidDate = paidDate;
                        changed = true;
                    }

                    if (invoice.Status != status)
                    {
                        invoice.Status = status;
                        changed = true;
                    }

                    if (invoice.FilePath != filePath)
                    {
                        invoice.FilePath = filePath;
                        changed = true;
                    }

                    if (invoice.Notes != notes)
                    {
                        invoice.Notes = notes;
                        changed = true;
                    }

                    if (invoice.IsPaid != isPaid)
                    {
                        invoice.IsPaid = isPaid;
                        changed = true;
                    }

                    if (changed) updated++;
                }
                else
                {
                    // New invoice – NOTE: no "var" here, we reuse the existing 'invoice' variable
                    invoice = new Invoice
                    {
                        InvoiceNumber = invoiceNo,
                        ProjectCode = jobNo,
                        JobName = jobName,
                        ClientName = clientName,
                        InvoiceDate = invoiceDate,
                        PaymentTermsDays = paymentTermsDays,
                        DueDate = dueDate,
                        NetAmount = netAmount,
                        VatRate = vatRate,
                        VatAmount = vatAmount,
                        GrossAmount = grossAmount,
                        PaymentAmount = paymentAmount,
                        PaidDate = paidDate,
                        Status = status,
                        FilePath = filePath,
                        Notes = notes,
                        IsPaid = isPaid
                    };

                    db.Invoices.Add(invoice);
                    existing[key] = invoice;
                    created++;
                }
            }

            db.SaveChanges();
            Console.WriteLine($"  Invoices: {created} created, {updated} updated (no deletes).");
        }

        private static string NormalizeInvoiceNumber(string input)
        {
            var raw = (input ?? "").Trim();

            if (raw.Length == 0)
                return raw;

            // Split numeric prefix + optional suffix (e.g. 523a)
            int i = 0;
            while (i < raw.Length && char.IsDigit(raw[i]))
                i++;

            var numericPart = raw.Substring(0, i);
            var suffix = raw.Substring(i); // may be empty or letters

            // If there's no numeric part, leave it untouched
            if (numericPart.Length == 0)
                return raw;

            // Pad to minimum 4 digits, but do not truncate if longer
            var padded = numericPart.Length < 4
                ? numericPart.PadLeft(4, '0')
                : numericPart;

            return padded + suffix;
        }

        private static void ImportApplicationSchedule(AppDbContext db, string schedulePath)
        {
            Console.WriteLine();
            Console.WriteLine("Importing application schedule...");

            using var workbook = new XLWorkbook(schedulePath);
            // Use first worksheet; headers in row 1
            var sheet = workbook.Worksheet(1);

            var headerRow = sheet.FirstRowUsed().RowNumber();
            var lastColumn = sheet.Row(headerRow).LastCellUsed().Address.ColumnNumber;

            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= lastColumn; col++)
            {
                var header = sheet.Cell(headerRow, col).GetString().Trim();
                if (!string.IsNullOrEmpty(header) && !columns.ContainsKey(header))
                {
                    columns[header] = col;
                }
            }

            string GetString(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return "";
                return row.Cell(col).GetString().Trim();
            }

            DateTime? GetDate(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return null;
                var cell = row.Cell(col);

                DateTime dt;

                if (cell.TryGetValue<DateTime>(out dt))
                    return AsUtcDate(dt);

                var txt = cell.GetString().Trim();
                if (DateTime.TryParse(txt, out dt))
                    return AsUtcDate(dt);

                return null;
            }

            int? GetInt(IXLRow row, string headerName)
            {
                if (!columns.TryGetValue(headerName, out var col)) return null;
                var cell = row.Cell(col);

                if (cell.TryGetValue<int>(out var i))
                    return i;

                var txt = cell.GetString().Trim();
                if (int.TryParse(txt, out i))
                    return i;

                return null;
            }

            var firstDataRow = headerRow + 1;
            var lastDataRow = sheet.LastRowUsed().RowNumber();

            // Simple approach: rebuild the table each import
            db.ApplicationSchedules.RemoveRange(db.ApplicationSchedules);
            db.SaveChanges();

            int created = 0;

            for (int r = firstDataRow; r <= lastDataRow; r++)
            {
                var row = sheet.Row(r);

                var projectNo = GetString(row, "ProjectNo");
                if (string.IsNullOrWhiteSpace(projectNo))
                    continue;

                var scheduleType = GetString(row, "ScheduleType");
                if (string.IsNullOrWhiteSpace(scheduleType))
                    continue;

                var appDate = GetDate(row, "ApplicationSubmissionDate");
                var valEnd = GetDate(row, "ValuationPeriodEnd");
                var payDue = GetDate(row, "PaymentDueDate");
                var payNotice = GetDate(row, "PaymentNoticeDueDate");
                var payLess = GetDate(row, "PayLessNoticeDueDate");
                var finalPay = GetDate(row, "FinalPaymentDate");

                var ruleType = GetString(row, "RuleType");
                var ruleValue = GetInt(row, "RuleValue");
                var notes = GetString(row, "Notes");

                var entry = new ApplicationSchedule
                {
                    ProjectCode = projectNo.Trim(),
                    ScheduleType = scheduleType.Trim(),
                    ApplicationSubmissionDate = appDate,
                    ValuationPeriodEnd = valEnd,
                    PaymentDueDate = payDue,
                    PaymentNoticeDueDate = payNotice,
                    PayLessNoticeDueDate = payLess,
                    FinalPaymentDate = finalPay,
                    RuleType = string.IsNullOrWhiteSpace(ruleType) ? null : ruleType.Trim(),
                    RuleValue = ruleValue,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
                };

                db.ApplicationSchedules.Add(entry);
                created++;
            }

            db.SaveChanges();
            Console.WriteLine($"  Application schedules: {created} rows imported (full refresh).");
        }

        private static string GetInitialsFromFilename(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var start = name.IndexOf('(');
            var end = name.IndexOf(')');

            if (start >= 0 && end > start)
            {
                return name.Substring(start + 1, end - start - 1).Trim();
            }

            return string.Empty;
        }

        private static (string category, bool isRealProject) ClassifyJobName(string jobNameOrNumber)
        {
            var n = (jobNameOrNumber ?? "").Trim();

            // Real projects: PNxxxx / SWxxxx etc.
            if (ProjectCodeHelpers.GetBaseProjectCode(n) != null)
                return ("Project", true);

            var lower = n.ToLowerInvariant();

            // Overhead buckets
            if (lower.Contains("bank holiday") || lower.Equals("holiday") || lower.Contains(" holiday"))
                return ("Overhead", false);

            if (lower.Contains("sick"))
                return ("Overhead", false);

            // Pre-contract / sales effort
            if (lower.Contains("fee proposal") || lower.Contains("tender"))
                return ("Sales", false);

            // Internal/admin buckets
            if (lower.Contains("business") || lower.Contains("admin"))
                return ("Internal", false);

            return ("Uncategorised", false);
        }

        private static string GetInvoiceKey(string invoiceNumber, string? projectCode, DateTime invoiceDate, decimal netAmount)
        {
            var num = invoiceNumber?.Trim() ?? "";
            var code = projectCode?.Trim() ?? "";
            var datePart = invoiceDate == DateTime.MinValue
                ? ""
                : invoiceDate.ToString("yyyy-MM-dd");
            var netPart = netAmount.ToString("F2", CultureInfo.InvariantCulture);

            return $"{num}|{code}|{datePart}|{netPart}";
        }

        private static void PrintWorkerHoursAndCostForMonth(AppDbContext db, int year, int month)
        {
            var firstDay = DateTime.SpecifyKind(new DateTime(year, month, 1), DateTimeKind.Utc);
            var lastDayExclusive = firstDay.AddMonths(1);

            Console.WriteLine();
            Console.WriteLine($"Hours and cost per worker for {firstDay:MMMM yyyy}:");

            var workers = db.Workers.OrderBy(w => w.Initials).ToList();

            // Collect results first
            var results = new List<(string Initials, decimal Hours, decimal Cost)>();

            foreach (var worker in workers)
            {
                var entries = db.TimesheetEntries
                    .Where(e => e.WorkerId == worker.Id &&
                                e.Date >= firstDay &&
                                e.Date < lastDayExclusive)
                    .ToList();

                if (!entries.Any())
                    continue;

                decimal totalHours = 0m;
                decimal totalCost = 0m;

                foreach (var entry in entries)
                {
                    totalHours += entry.Hours;

                    var ratePerHour = WorkerRateHelpers.GetRateForWorkerOnDate_DbLookup(db, worker.Id, entry.Date);

                    if (ratePerHour.HasValue)
                    {
                        totalCost += entry.Hours * ratePerHour.Value;
                    }
                    else
                    {
                        Console.WriteLine($"  WARNING: No rate found for {worker.Initials} on {entry.Date:yyyy-MM-dd}.");
                    }
                }

                // Store result for sorting
                results.Add((worker.Initials, totalHours, totalCost));
            }

            // Sort by total labour cost DESC
            var sorted = results
                .OrderByDescending(r => r.Cost)
                .ThenByDescending(r => r.Hours) // optional secondary sort
                .ToList();

            // Print sorted output
            foreach (var r in sorted)
            {
                Console.WriteLine($"{r.Initials}: {r.Hours} hours, £{r.Cost:F2} labour cost");
            }
        }

        private static void PrintProjectCostVsInvoiced(AppDbContext db)
        {
            Console.WriteLine();
            Console.WriteLine("Project labour cost vs invoiced (all time):");

            // Cache invoices grouped by ProjectCode (e.g. "PN0051")
            var invoicesByCode = db.Invoices
                .Where(i => !string.IsNullOrWhiteSpace(i.ProjectCode))
                .AsEnumerable() // switch to in-memory so we can Trim safely
                .GroupBy(i => i.ProjectCode!.Trim())
                .ToDictionary(g => g.Key, g => g.ToList());

            var projects = db.Projects
                .OrderBy(p => p.JobNameOrNumber)
                .ToList();

            foreach (var project in projects)
            {
                // Derive "PN0051" from "PN0051 - PIMCO"
                var baseCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
                var hasProjectCode = !string.IsNullOrEmpty(baseCode);

                // All timesheet entries for this project
                var entries = db.TimesheetEntries
                    .Where(e => e.ProjectId == project.Id)
                    .ToList();

                // Skip projects entirely if they have neither labour nor a mappable project code
                if (!entries.Any() && !hasProjectCode)
                    continue;

                decimal totalLabourCost = 0m;

                foreach (var entry in entries)
                {
                    var ratePerHour = WorkerRateHelpers.GetRateForWorkerOnDate_DbLookup(db, entry.WorkerId, entry.Date);
                    if (ratePerHour.HasValue)
                    {
                        totalLabourCost += entry.Hours * ratePerHour.Value;
                    }
                    else
                    {
                        Console.WriteLine(
                            $"  WARNING: No rate for worker {entry.WorkerId} on {entry.Date:yyyy-MM-dd} (project {project.JobNameOrNumber}).");
                    }
                }

                // Get invoices for this base code
                List<Invoice> invoicesForProject = new();

                if (hasProjectCode && invoicesByCode.TryGetValue(baseCode!, out var list))
                {
                    invoicesForProject = list;
                }

                decimal totalInvoicedNet = invoicesForProject.Sum(i => i.NetAmount);
                decimal totalInvoicedGross = invoicesForProject.Sum(i => i.GrossAmount ?? 0m);

                decimal profitNet = totalInvoicedNet - totalLabourCost;
                decimal profitGross = totalInvoicedGross - totalLabourCost;

                var label = hasProjectCode
                    ? $"{project.JobNameOrNumber} [{baseCode}]"
                    : $"{project.JobNameOrNumber} [no project code]";

                Console.WriteLine(
                    $"{label}: Labour £{totalLabourCost:F2}, " +
                    $"Invoiced net £{totalInvoicedNet:F2}, " +
                    $"Invoiced gross £{totalInvoicedGross:F2}, " +
                    $"Profit (net) £{profitNet:F2}, Profit (gross) £{profitGross:F2}");
            }
        }

        // Used once i think, might still need it
        private static void BackfillProjectClassification(AppDbContext db)
        {
            int changed = 0;

            foreach (var p in db.Projects)
            {
                // Only fill blanks / defaults.
                // If you’ve already set something manually later, don’t touch it.
                if (string.IsNullOrWhiteSpace(p.Category) || p.Category == "Uncategorised")
                {
                    var (cat, isReal) = ClassifyJobName(p.JobNameOrNumber);
                    p.Category = cat;
                    p.IsRealProject = isReal;
                    changed++;
                }
            }

            if (changed > 0)
            {
                db.SaveChanges();
                Console.WriteLine($"  Backfilled classification for {changed} projects.");
            }
        }

        private static int EnsureCompanyId(AppDbContext db, Dictionary<string, Company> byCode, string raw)
        {
            var code = (raw ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("CompanyCode code is required but was blank in import.");

            // Normalize (optional but recommended)
            code = code.ToUpperInvariant();

            if (byCode.TryGetValue(code, out var existing))
                return existing.Id;

            var c = new Company
            {
                Code = code,
                Name = code,
                IsActive = true
            };

            db.Companies.Add(c);
            db.SaveChanges(); // so we get Id

            byCode[code] = c;
            return c.Id;
        }

        static string MaskConnectionStringForLog(string provider, string cs)
        {
            if (string.IsNullOrWhiteSpace(cs)) return "";

            // light masking for logs (don’t print passwords)
            // handles "Password=..." and "Pwd=..."
            string Mask(string input, string key)
            {
                var idx = input.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return input;

                var start = idx + key.Length;
                var end = input.IndexOf(';', start);
                if (end < 0) end = input.Length;

                return input.Substring(0, start) + "***" + input.Substring(end);
            }

            var masked = cs;
            masked = Mask(masked, "Password=");
            masked = Mask(masked, "Pwd=");

            return masked;
        }

        static DateTime AsUtc(DateTime dt)
            => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        // Use this for Excel “date-only” values (rate effective dates, timesheet dates, etc.)
        static DateTime AsUtcDate(DateTime dt)
            => DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);

    }
}
