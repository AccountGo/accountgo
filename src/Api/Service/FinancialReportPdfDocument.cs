// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Services.Financial;

namespace Api.Service
{
    /// <summary>QuestPDF layout for a composed financial statement. The figures come from <see cref="FinancialReportComposer"/>.</summary>
    public sealed class FinancialReportPdfDocument : IDocument
    {
        private const string Ink = "#0F172A";
        private const string Secondary = "#475569";
        private const string Muted = "#64748B";
        private const string Rule = "#E2E8F0";
        private const string Shade = "#F8FAFC";
        private const string TotalShade = "#F1F5F9";

        private readonly FinancialReportPrint _report;

        public FinancialReportPdfDocument(FinancialReportPrint report)
        {
            _report = report;
        }

        public string FileStem => _report.FileStem;

        public DocumentMetadata GetMetadata() => new()
        {
            Title = _report.Heading + " — " + _report.Title,
            Author = _report.Heading,
            Subject = _report.Title + " — " + _report.PeriodLabel,
            Creator = "AccountGo",
        };

        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(_report.CurrencyCode is "USD" or "CAD" ? PageSizes.Letter : PageSizes.A4);
                page.Margin(36);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontFamily(Fonts.Lato).FontSize(9).FontColor(Ink));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.PaddingBottom(10).BorderBottom(1).BorderColor(Rule).Column(column =>
            {
                column.Item().Text(_report.Heading).FontSize(16).Bold();
                if (!string.IsNullOrWhiteSpace(_report.TradingAs))
                    column.Item().Text("Trading as " + _report.TradingAs).FontSize(9).FontColor(Secondary);
                column.Item().PaddingTop(2).Text(_report.Title).FontSize(12).SemiBold().FontColor(Secondary);
                if (!string.IsNullOrWhiteSpace(_report.Subtitle))
                    column.Item().Text(_report.Subtitle).FontSize(9).FontColor(Muted);
                column.Item().PaddingTop(4).Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(9).FontColor(Muted));
                    text.Span(_report.PeriodLabel);
                    if (!string.IsNullOrWhiteSpace(_report.BasisLabel))
                    {
                        text.Span("  ·  ");
                        text.Span(_report.BasisLabel);
                    }
                    text.Span("  ·  ");
                    text.Span(_report.CurrencyCode);
                });
                if (!string.IsNullOrWhiteSpace(_report.ComparativePeriodLabel))
                    column.Item().Text(_report.ComparativePeriodLabel).FontSize(9).FontColor(Muted);
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.PaddingTop(8).BorderTop(1).BorderColor(Rule).DefaultTextStyle(style => style.FontSize(8).FontColor(Muted)).Row(row =>
            {
                row.RelativeItem().AlignLeft().Text("Unaudited  ·  " + _report.GeneratedLabel);
                row.RelativeItem().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
                row.RelativeItem().AlignRight().Text(_report.CurrencyCode);
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingTop(12).Column(column =>
            {
                if (!string.IsNullOrWhiteSpace(_report.BalanceStatus))
                {
                    column.Item().Text(_report.BalanceStatus).FontSize(9).SemiBold()
                        .FontColor(_report.BalanceStatus == "In balance" ? "#047857" : "#B91C1C");
                }
                if (!string.IsNullOrWhiteSpace(_report.Notice))
                    column.Item().PaddingTop(6).Text(_report.Notice).FontSize(9).FontColor(Secondary);
                if (!string.IsNullOrWhiteSpace(_report.EmptyNote))
                    column.Item().PaddingTop(6).Text(_report.EmptyNote).FontSize(9).Italic().FontColor(Muted);
                if (!string.IsNullOrWhiteSpace(_report.Footnote))
                    column.Item().PaddingTop(6).Text(_report.Footnote).FontSize(8).FontColor(Muted).Italic();

                if (_report.CashMovement != null)
                    column.Item().PaddingTop(12).Element(ComposeMovement);
                else if (_report.Lines.Count > 0)
                    column.Item().PaddingTop(12).Element(ComposeLines);
            });
        }

        private void ComposeMovement(IContainer container)
        {
            var movement = _report.CashMovement!;
            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("Opening cash").FontColor(Secondary);
                    row.ConstantItem(110).AlignRight().Text(Money(movement.OpeningCash)).SemiBold();
                });
                column.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(72);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.ConstantColumn(78);
                        columns.ConstantColumn(78);
                    });
                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Date", false);
                        HeaderCell(header.Cell(), "Description", false);
                        HeaderCell(header.Cell(), "Account", false);
                        HeaderCell(header.Cell(), "Counterpart", false);
                        HeaderCell(header.Cell(), "Money in", true);
                        HeaderCell(header.Cell(), "Money out", true);
                    });
                    foreach (var line in movement.Lines)
                    {
                        BodyCell(table.Cell(), line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), false, false);
                        BodyCell(table.Cell(), line.Description ?? "", false, false);
                        BodyCell(table.Cell(), line.AccountName, false, false);
                        BodyCell(table.Cell(), line.CounterpartAccountName, false, false);
                        BodyCell(table.Cell(), line.MoneyIn == 0m ? "" : Money(line.MoneyIn), true, false);
                        BodyCell(table.Cell(), line.MoneyOut == 0m ? "" : Money(line.MoneyOut), true, false);
                    }
                });
                column.Item().PaddingTop(8).Element(c => SummaryRow(c, "Total in", movement.TotalIn, false));
                column.Item().Element(c => SummaryRow(c, "Total out", movement.TotalOut, false));
                column.Item().Element(c => SummaryRow(c, "Closing cash", movement.ClosingCash, true));
            });
        }

        private void SummaryRow(IContainer container, string label, decimal amount, bool bold)
        {
            container.BorderTop(bold ? 1 : 0).BorderColor(Rule).PaddingVertical(3).Row(row =>
            {
                var text = row.RelativeItem().Text(label).FontColor(Secondary);
                if (bold) text.SemiBold();
                var value = row.ConstantItem(110).AlignRight().Text(Money(amount));
                if (bold) value.SemiBold();
            });
        }

        private void ComposeLines(IContainer container)
        {
            var compare = _report.ShowComparison;
            var trial = _report.Lines.Any(line => line.Debit.HasValue || line.Credit.HasValue);
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(compare ? 52 : 64);
                    columns.RelativeColumn();
                    if (trial)
                    {
                        columns.ConstantColumn(88);
                        columns.ConstantColumn(88);
                    }
                    else if (compare)
                    {
                        columns.ConstantColumn(78);
                        columns.ConstantColumn(78);
                        columns.ConstantColumn(72);
                        columns.ConstantColumn(52);
                    }
                    else
                    {
                        columns.ConstantColumn(100);
                    }
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Code", false);
                    HeaderCell(header.Cell(), "Account", false);
                    if (trial)
                    {
                        HeaderCell(header.Cell(), "Debit", true);
                        HeaderCell(header.Cell(), "Credit", true);
                    }
                    else if (compare)
                    {
                        HeaderCell(header.Cell(), "Current", true);
                        HeaderCell(header.Cell(), "Prior", true);
                        HeaderCell(header.Cell(), "Change", true);
                        HeaderCell(header.Cell(), "Change %", true);
                    }
                    else
                    {
                        HeaderCell(header.Cell(), "Amount", true);
                    }
                });

                foreach (var line in _report.Lines)
                {
                    if (line.Kind == FinancialReportLineKind.Section && line.Label == _report.Notice)
                        continue;
                    var span = trial ? 4u : compare ? 6u : 3u;
                    if (line.Kind == FinancialReportLineKind.Section)
                    {
                        table.Cell().ColumnSpan(span).Background(Shade).PaddingVertical(5).PaddingHorizontal(4)
                            .Text(line.Label).SemiBold().FontColor(Secondary);
                        continue;
                    }

                    var total = line.Kind == FinancialReportLineKind.Total;
                    BodyCell(table.Cell(), line.Code, false, total);
                    BodyCell(table.Cell(), line.Label, false, total);
                    if (trial)
                    {
                        BodyCell(table.Cell(), Money(line.Debit), true, total);
                        BodyCell(table.Cell(), Money(line.Credit), true, total);
                    }
                    else if (compare)
                    {
                        BodyCell(table.Cell(), Money(line.Amount), true, total);
                        BodyCell(table.Cell(), Money(line.ComparativeAmount), true, total);
                        BodyCell(table.Cell(), Money(line.Change), true, total);
                        BodyCell(table.Cell(), Percent(line.ChangePercent), true, total);
                    }
                    else
                    {
                        BodyCell(table.Cell(), Money(line.Amount), true, total);
                    }
                }
            });
        }

        private static void HeaderCell(IContainer cell, string text, bool right)
        {
            var item = cell.Background(TotalShade).BorderBottom(1).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(4);
            var content = right ? item.AlignRight() : item.AlignLeft();
            content.Text(text).FontSize(8).SemiBold().FontColor(Secondary);
        }

        private static void BodyCell(IContainer cell, string? text, bool right, bool total)
        {
            var item = cell.BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(4);
            if (total)
                item = item.Background(TotalShade).BorderTop(0.6f);
            var content = right ? item.AlignRight() : item.AlignLeft();
            var written = content.Text(text ?? "");
            if (total)
                written.SemiBold();
        }

        private string Money(decimal? amount)
        {
            if (amount is null)
                return "";
            var value = amount.Value;
            var symbol = _report.CurrencyCode switch
            {
                "USD" => "$",
                "EUR" => "€",
                "GBP" => "£",
                "CAD" => "CA$",
                "AUD" => "AU$",
                "SGD" => "S$",
                _ => _report.CurrencyCode + " ",
            };
            var text = symbol + Math.Abs(value).ToString("N2", CultureInfo.InvariantCulture);
            return value < 0m ? "(" + text + ")" : text;
        }

        private static string Percent(decimal? value)
        {
            if (value is null)
                return "—";
            return value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
