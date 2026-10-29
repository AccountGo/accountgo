// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Linq;
using Dto.Banking;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Api.Service
{
    public class BankReconciliationReportPdfDocument : IDocument
    {
        private const string Ink = "#0F172A";
        private const string Secondary = "#475569";
        private const string Muted = "#64748B";
        private const string Rule = "#E2E8F0";
        private const string Shade = "#F1F5F9";

        private readonly BankReconciliationReportDto _report;
        private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

        public BankReconciliationReportPdfDocument(BankReconciliationReportDto report)
        {
            _report = report;
        }

        public string FileStem =>
            $"BankReconciliation-{_report.BankName.Replace(' ', '-')}-{_report.StatementEndDate:yyyy-MM-dd}";

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"Bank reconciliation — {_report.BankName} — {_report.StatementEndDate:yyyy-MM-dd}",
            Author = _report.CompanyName,
            Subject = "Bank reconciliation report",
            Creator = "AccountGo",
        };

        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(_report.CurrencyCode is "USD" or "CAD" ? PageSizes.Letter : PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontFamily(Fonts.Lato).FontSize(9.5f).FontColor(Ink));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().DefaultTextStyle(x => x.FontSize(8).FontColor(Muted))
                    .Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.PaddingBottom(16).BorderBottom(1).BorderColor(Rule).Column(column =>
            {
                column.Item().Text(_report.CompanyName).FontSize(16).Bold();
                column.Item().Text("BANK RECONCILIATION").FontSize(12).SemiBold().FontColor(Secondary);
                column.Item().PaddingTop(4).Text($"{_report.BankName} · Statement end {_report.StatementEndDate:yyyy-MM-dd}")
                    .FontSize(9).FontColor(Muted);
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingTop(16).Column(column =>
            {
                column.Item().Element(ComposeCleared);
                column.Item().PaddingTop(18).Element(ComposeSummary);
                column.Item().PaddingTop(18).Element(c => ComposeLineSection(c, "Outstanding items", _report.OutstandingItems));
                column.Item().PaddingTop(18).Element(c => ComposeLineSection(
                    c,
                    "Posted after this reconciliation was finished",
                    _report.PostedAfterFinishItems));
                column.Item().PaddingTop(18).Element(ComposeSignOff);
            });
        }

        private void ComposeCleared(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Text("Cleared").FontSize(11).Bold();
                column.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                    });

                    void Row(string label, decimal amount, bool bold = false)
                    {
                        table.Cell().PaddingVertical(3).Text(label).FontColor(Secondary).SemiBold();
                        var cell = table.Cell().PaddingVertical(3).AlignRight();
                        if (bold)
                            cell.Text(Money(amount)).Bold();
                        else
                            cell.Text(Money(amount));
                    }

                    Row("Beginning balance", _report.BeginningBalance);
                    if (_report.OpeningBalanceLine != null)
                    {
                        var ob = _report.OpeningBalanceLine;
                        Row(ob.Type, ob.In != 0m ? ob.In : ob.Out);
                    }
                    Row("Cleared deposits", _report.ClearedDeposits);
                    Row("Cleared payments", _report.ClearedPayments);
                    Row("Cleared balance", _report.ClearedBalance, bold: true);
                    Row("Statement ending balance", _report.StatementEndingBalance, bold: true);
                });
            });
        }

        private void ComposeSummary(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Text("Summary").FontSize(11).Bold();
                column.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                    });

                    void Row(string label, decimal amount, bool bold = false)
                    {
                        table.Cell().PaddingVertical(3).Text(label).FontColor(Secondary).SemiBold();
                        var cell = table.Cell().PaddingVertical(3).AlignRight();
                        if (bold)
                            cell.Text(Money(amount)).Bold();
                        else
                            cell.Text(Money(amount));
                    }

                    Row("Balance per bank statement", _report.StatementEndingBalance);
                    Row("Add: Deposits in transit", _report.DepositsInTransit);
                    Row("Less: Outstanding payments", _report.OutstandingPayments);
                    Row("Adjusted bank balance", _report.AdjustedBankBalance, bold: true);

                    table.Cell().PaddingTop(10).PaddingVertical(3).Text("Balance per books").FontColor(Secondary).SemiBold();
                    table.Cell().PaddingTop(10).PaddingVertical(3).AlignRight().Text(Money(_report.BooksBalance));

                    table.Cell().PaddingVertical(3).Text("Difference").FontColor(Secondary).SemiBold();
                    table.Cell().PaddingVertical(3).AlignRight().Text(Money(_report.Difference)).Bold();
                });
            });
        }

        private void ComposeLineSection(
            IContainer container,
            string title,
            System.Collections.Generic.IReadOnlyList<BankReconciliationReportLineDto> items)
        {
            container.Column(column =>
            {
                column.Item().Text(title).FontSize(11).Bold();
                if (items.Count == 0)
                {
                    column.Item().PaddingTop(6).Text("None.").FontColor(Muted);
                    return;
                }

                column.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(70);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.ConstantColumn(70);
                        columns.ConstantColumn(50);
                    });

                    void Head(string label) =>
                        table.Cell().Background(Shade).Padding(4).Text(label).FontSize(8).SemiBold().FontColor(Muted);

                    Head("Date");
                    Head("Type");
                    Head("Reference");
                    Head("Amount");
                    Head("Age");

                    foreach (var item in items)
                    {
                        table.Cell().Padding(4).Text(item.Date.ToString("yyyy-MM-dd", _culture));
                        table.Cell().Padding(4).Text(item.Type);
                        table.Cell().Padding(4).Text(item.Reference ?? "—").FontColor(Secondary);
                        var amount = item.In != 0m ? item.In : -item.Out;
                        table.Cell().Padding(4).AlignRight().Text(Money(amount));
                        table.Cell().Padding(4).AlignRight().Text($"{item.AgeDays}d");
                    }
                });
            });
        }

        private void ComposeSignOff(IContainer container)
        {
            var preparedWhen = _report.PreparedAt?.ToString("yyyy-MM-dd HH:mm' UTC'", _culture) ?? "—";
            var reviewedName = string.IsNullOrWhiteSpace(_report.ReviewedBy) ? string.Empty : _report.ReviewedBy.Trim();
            var reviewedWhen = _report.ReviewedAt?.ToString("yyyy-MM-dd HH:mm' UTC'", _culture);

            container.BorderTop(1).BorderColor(Rule).PaddingTop(10).Column(column =>
            {
                column.Item().Text($"Prepared by {_report.PreparedBy ?? "—"} at {preparedWhen}")
                    .FontSize(8.5f).FontColor(Muted);
                if (!string.IsNullOrEmpty(reviewedName) || reviewedWhen != null)
                {
                    column.Item().Text($"Reviewed by {reviewedName} at {reviewedWhen ?? "—"}")
                        .FontSize(8.5f).FontColor(Muted);
                }
                else
                {
                    column.Item().Text("Reviewed by").FontSize(8.5f).FontColor(Muted);
                }
            });
        }

        private string Money(decimal value) =>
            string.Create(_culture, $"{_report.CurrencyCode} {value:0.00}");
    }
}
