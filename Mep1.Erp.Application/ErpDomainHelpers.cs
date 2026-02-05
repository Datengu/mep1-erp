using System;
using System.Linq;
using Mep1.Erp.Core;           // entities
using Mep1.Erp.Infrastructure; // AppDbContext

namespace Mep1.Erp.Application
{
    public static class ProjectCodeHelpers
    {
        // PN0051 - Something  => "PN0051"
        // SW0123 – Something  => "SW0123"
        // Holiday / Sick / Business => null
        private static readonly char[] CodeSeparators = { ' ', '-', '–' };

        public static string? GetBaseProjectCode(string? jobNameOrNumber)
        {
            if (string.IsNullOrWhiteSpace(jobNameOrNumber))
                return null;

            var text = jobNameOrNumber.Trim();

            // Take everything up to the first space / dash / en-dash
            int idx = text.IndexOfAny(CodeSeparators);
            var code = (idx > 0 ? text[..idx] : text).Trim();

            if (code.StartsWith("PN", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("SW", StringComparison.OrdinalIgnoreCase))
                return code;

            return null;
        }

        /// <summary>
        /// Extracts the job name part from JobNameOrNumber.
        /// "PN0051 - Biggin Hill" => "Biggin Hill"
        /// "PN0051 Biggin Hill"   => "Biggin Hill"
        /// "PN0051"               => null
        /// </summary>
        public static string? GetJobName(string? jobNameOrNumber)
        {
            if (string.IsNullOrWhiteSpace(jobNameOrNumber))
                return null;

            var text = jobNameOrNumber.Trim();

            var baseCode = GetBaseProjectCode(text);
            if (baseCode == null)
                return null;

            // Remove the base code from the start
            var remainder = text.Substring(baseCode.Length).TrimStart();

            // Trim common separators
            remainder = remainder.TrimStart(' ', '-', '–');

            return string.IsNullOrWhiteSpace(remainder)
                ? null
                : remainder;
        }
    }

    public static class WorkerRateHelpers
    {
        // Centralised rate lookup so Desktop + Importer + Reporting agree.
        // Uses the same "ValidFrom inclusive, ValidTo exclusive (when not null)" pattern you already use.
        public static decimal? GetRateForWorkerOnDate_DbLookup(AppDbContext db, int workerId, DateTime workDate)
        {
            var rate = db.WorkerRates
                .Where(r => r.WorkerId == workerId &&
                            workDate >= r.ValidFrom &&
                            (r.ValidTo == null || workDate < r.ValidTo))
                .OrderByDescending(r => r.ValidFrom)
                .FirstOrDefault();

            return rate?.RatePerHour;
        }
    }

    public static class DateHelpers
    {
        // Monday as start of week (matches your dashboard grouping intent)
        public static DateTime GetWeekStartMonday(DateTime date)
        {
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }
    }

    public static class InvoiceHelpers
    {
        public static bool IsVoidOrCancelled(this Invoice i)
        {
            var status = i.Status ?? string.Empty;

            return status.IndexOf("void", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("write off", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("settled", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("not invoiced", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static decimal GetOutstandingNet(this Invoice i)
        {
            if (i.IsVoidOrCancelled())
                return 0m;

            var paid = i.PaymentAmount ?? 0m;
            var remaining = i.NetAmount - paid;

            return remaining > 0m ? remaining : 0m;
        }

        public static decimal GetOutstandingGross(this Invoice i)
        {
            if (i.IsVoidOrCancelled())
                return 0m;

            var gross = i.GrossAmount ?? 0m;
            var net = i.NetAmount;

            // If we don't have sensible gross/net, just fall back to net logic
            if (gross <= 0m || net <= 0m)
                return i.GetOutstandingNet();

            var remainingNet = i.GetOutstandingNet();
            if (remainingNet <= 0m)
                return 0m;

            // Scale gross by the same proportion that remains unpaid on net
            var factor = remainingNet / net;
            return gross * factor;
        }
    }

    public static class RateHelpers
    {
        public static decimal GetRateOnDate(
            IReadOnlyList<WorkerRate> ratesForWorker,
            DateTime workDate)
        {
            foreach (var r in ratesForWorker)
            {
                if (workDate >= r.ValidFrom && (r.ValidTo == null || workDate < r.ValidTo))
                    return r.RatePerHour;
            }
            return 0m;
        }
    }

}
