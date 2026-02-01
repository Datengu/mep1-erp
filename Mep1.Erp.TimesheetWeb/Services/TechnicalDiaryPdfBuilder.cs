using Mep1.Erp.Core.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using System.Reflection;

namespace Mep1.Erp.TimesheetWeb.Services;

public sealed class TechnicalDiaryPdfBuilder
{
    private static bool _fontRegistered;

    private static void EnsureSignatureFontRegistered()
    {
        if (_fontRegistered) return;

        // Works for both local run and VPS publish:
        // published output will include wwwroot/assets/VLADIMIR.TTF
        var fontPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "VLADIMIR.TTF");

        if (!File.Exists(fontPath))
        {
            // Fallback attempt for some hosting layouts (rare, but harmless)
            fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "VLADIMIR.TTF");
        }

        if (File.Exists(fontPath))
        {
            FontManager.RegisterFont(File.OpenRead(fontPath));
            _fontRegistered = true;
        }
        // If it doesn't exist, we just won't apply the font and it will render in default.
    }

    public byte[] BuildWeekPdf(
        string workerName,
        string workerSignatureName,
        string checkedBySignatureName,
        DateTime weekEndingFriday,
        IReadOnlyList<TimesheetEntrySummaryDto> entries)
    {
        // Defensive: ensure only the week’s rows are passed in
        var ordered = entries.OrderBy(e => e.Date).ToList();
        var totalHours = ordered.Sum(e => e.Hours);

        EnsureSignatureFontRegistered();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("Technical Diary").SemiBold().FontSize(16);

                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Text($"Name: {workerName}");
                        row.RelativeItem().AlignRight().Text($"Week Ending: {weekEndingFriday:dd/MM/yyyy}");
                    });

                    col.Item().PaddingTop(8).LineHorizontal(1);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(72);   // Date
                            cols.ConstantColumn(40);   // Day
                            cols.ConstantColumn(45);   // Hours
                            cols.RelativeColumn(1.2f); // Company
                            cols.ConstantColumn(40);   // Code
                            cols.RelativeColumn(1.2f); // Job Name/No
                            cols.RelativeColumn(2.0f); // Job Task Description
                            cols.ConstantColumn(60);   // CCF Ref
                        });

                        // Header
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Date");
                            header.Cell().Element(HeaderCell).Text("Day");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Hours");
                            header.Cell().Element(HeaderCell).Text("CompanyCode");
                            header.Cell().Element(HeaderCell).Text("Code");
                            header.Cell().Element(HeaderCell).Text("Job Name/No.");
                            header.Cell().Element(HeaderCell).Text("Job Task Description");
                            header.Cell().Element(HeaderCell).Text("CCF Ref.");

                            static IContainer HeaderCell(IContainer c) =>
                                c.DefaultTextStyle(x => x.SemiBold())
                                 .PaddingVertical(4)
                                 .PaddingHorizontal(3)
                                 .BorderBottom(1);
                        });

                        foreach (var e in ordered)
                        {
                            table.Cell().Element(BodyCell).Text(e.Date.ToString("dd/MM/yyyy"));
                            table.Cell().Element(BodyCell).Text(e.Date.ToString("ddd"));
                            table.Cell().Element(BodyCell).AlignRight().Text(e.Hours.ToString("0.00"));
                            table.Cell().Element(BodyCell).Text(e.ProjectCompanyCode ?? "");
                            table.Cell().Element(BodyCell).Text(e.Code ?? "");
                            table.Cell().Element(BodyCell).Text(e.JobKey ?? "");
                            table.Cell().Element(BodyCell).Text(e.TaskDescription ?? "");
                            table.Cell().Element(BodyCell).Text(e.CcfRef ?? "");

                            static IContainer BodyCell(IContainer c) =>
                                c.PaddingVertical(3)
                                 .PaddingHorizontal(3)
                                 .BorderBottom(1)
                                 .BorderColor(Colors.Grey.Lighten2);
                        }
                    });

                    col.Item().PaddingTop(10).AlignRight()
                        .Text($"Total Hours: {totalHours:0.00}")
                        .SemiBold();

                    col.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span("Signed: ");
                            // v1: italic “signature-like”. Later we can embed a real font.
                            txt.Span(workerSignatureName).Italic();
                        });

                        row.RelativeItem().AlignRight().Text(txt =>
                        {
                            txt.Span("Checked: ");
                            txt.Span(checkedBySignatureName)
                            .FontFamily("Vladimir Script")
                            .Italic();
                        });
                    });

                    col.Item().PaddingTop(10).LineHorizontal(1);

                    col.Item().PaddingTop(8).Text("Codes:").SemiBold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        void Row(string left, string right = "")
                        {
                            table.Cell().Text(left);
                            table.Cell().Text(right);
                        }

                        Row("P   = Programmed Drawing Input", "RD  = Record Drawings");
                        Row("IC  = Updating to Internal Comments", "S   = Surveys");
                        Row("EC  = Updating to External Comments", "T   = Training");
                        Row("GM  = General Management", "BIM = BIM Works");
                        Row("M   = Meetings", "DC  = Document Control");
                        Row("FP  = Fee Proposal", "BU  = Business Works");
                        Row("QA  = Drawing QA Check", "TP  = Tender Presentation");
                        Row("VO  = Variations", "SI  = Sick");
                        Row("HOL = Holiday", "");
                    });
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8));
                    text.Span("Generated: ");
                    text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                });
            });
        });

        return doc.GeneratePdf();
    }
}
