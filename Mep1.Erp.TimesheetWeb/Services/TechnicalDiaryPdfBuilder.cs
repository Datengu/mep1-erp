using Mep1.Erp.Core;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Mep1.Erp.TimesheetWeb.Services;

public sealed class TechnicalDiaryPdfBuilder
{
    public byte[] BuildWeekPdf(
        string workerName,
        string workerSignatureName,
        DateTime weekEndingSunday,
        IReadOnlyList<TimesheetEntrySummaryDto> entries)
    {
        // Defensive: ensure only the week’s rows are passed in
        var ordered = entries.OrderBy(e => e.Date).ToList();
        var totalHours = ordered.Sum(e => e.Hours);

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
                        row.RelativeItem().AlignRight().Text($"Week Ending: {weekEndingSunday:dd/MM/yyyy}");
                    });

                    col.Item().PaddingTop(8).LineHorizontal(1);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(60);   // Date
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
                            header.Cell().Element(HeaderCell).Text("Company");
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
                            table.Cell().Element(BodyCell).Text(e.ProjectCompany ?? "");
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

                        row.RelativeItem().AlignRight().Text("Checked: ____________________");
                    });

                    col.Item().PaddingTop(10).LineHorizontal(1);

                    col.Item().PaddingTop(8).Text("NB: Drawing codes to be detailed for all drawings produced and checked etc:");
                    col.Item().Text("WP = Work in Progress, F = For Approval / Comment, C = Construction, AB = As Built,");
                    col.Item().Text("VO = Variation Order, ME = Markups / Existing, M = Meetings");
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
