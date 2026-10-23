using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Api.Service
{
    /// <summary>Lays out a sales invoice as a print-ready PDF, independent of any browser.</summary>
    public class InvoicePdfDocument : IDocument
    {
        private const string Ink = "#0F172A";
        private const string Secondary = "#475569";
        private const string Muted = "#64748B";
        private const string Rule = "#E2E8F0";
        private const string Shade = "#F1F5F9";
        private const string Brand = "#059669";

        // Matches the web app's display symbols (src/Web/src/lib/currency.ts).
        private static readonly Dictionary<string, string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "$",
            ["PHP"] = "₱",
            ["CAD"] = "CA$",
            ["EUR"] = "€",
            ["GBP"] = "£",
            ["AUD"] = "AU$",
            ["SGD"] = "S$",
        };

        private readonly Dto.Sales.SalesInvoice _invoice;
        private readonly string _currencyCode;
        private readonly string _currencySymbol;

        public InvoicePdfDocument(Dto.Sales.SalesInvoice invoice, string? currencyCode)
        {
            _invoice = invoice;
            _currencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "USD" : currencyCode.ToUpperInvariant();
            _currencySymbol = CurrencySymbols.TryGetValue(_currencyCode, out var symbol) ? symbol : "$";
        }

        private string SellerName => string.IsNullOrWhiteSpace(_invoice.Seller.Name) ? "AccountGo" : _invoice.Seller.Name;

        public string InvoiceNumber =>string.IsNullOrWhiteSpace(_invoice.No) ? _invoice.Id.ToString(CultureInfo.InvariantCulture) : _invoice.No!;

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"Invoice {InvoiceNumber}",
            Author = SellerName,
            Subject = $"Invoice {InvoiceNumber} for {_invoice.CustomerName}",
            Creator = "AccountGo",
        };

        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                // North American workspaces print on Letter; everyone else on A4.
                page.Size(_currencyCode is "USD" or "CAD" ? PageSizes.Letter : PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontFamily(Fonts.Lato).FontSize(9.5f).FontColor(Ink));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.PaddingBottom(18).BorderBottom(1).BorderColor(Rule).PaddingBottom(18).Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    var seller = _invoice.Seller;
                    column.Item().Text(SellerName).FontSize(18).Bold();
                    foreach (var line in seller.AddressLines)
                        column.Item().Text(line).FontSize(8.5f).FontColor(Secondary);
                    var contact = string.Join(" · ", new[] { seller.Email, seller.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (contact.Length > 0)
                        column.Item().Text(contact).FontSize(8.5f).FontColor(Secondary);
                    if (!string.IsNullOrWhiteSpace(seller.TaxId))
                        column.Item().PaddingTop(2).Text($"Tax ID: {seller.TaxId}").FontSize(8.5f).FontColor(Muted);
                });

                row.ConstantItem(200).AlignRight().Column(column =>
                {
                    column.Item().AlignRight().Text("INVOICE").FontSize(22).ExtraBold().LetterSpacing(0.04f);
                    column.Item().AlignRight().Text($"#{InvoiceNumber}").FontSize(11).SemiBold().FontColor(Brand);
                    column.Item().PaddingTop(6).AlignRight().Element(ComposeStatusBadge);
                });
            });
        }

        private void ComposeStatusBadge(IContainer container)
        {
            var (label, color) = _invoice.Status switch
            {
                "Paid" => ("PAID", "#059669"),
                "Partially Paid" => ("PARTIALLY PAID", "#D97706"),
                "Overdue" => ("OVERDUE", "#E11D48"),
                "Void" => ("VOID", "#64748B"),
                "Draft" => ("DRAFT", "#64748B"),
                _ => ("OPEN", "#2563EB"),
            };

            container.Border(1).BorderColor(color).CornerRadius(4).PaddingVertical(2).PaddingHorizontal(8)
                .Text(label).FontSize(8).Bold().FontColor(color).LetterSpacing(0.08f);
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingTop(14).Column(column =>
            {
                column.Spacing(22);
                column.Item().Element(ComposeParties);
                column.Item().Element(ComposeLines);
                column.Item().ShowEntire().Element(ComposeTotals);

                if (_invoice.Payments is { Count: > 0 })
                    column.Item().ShowEntire().Element(ComposePayments);

                if (_invoice.Display.ShowNotes && !string.IsNullOrWhiteSpace(_invoice.Notes))
                    column.Item().ShowEntire().Element(c => ComposeNote(c, "NOTES", _invoice.Notes!));

                if (_invoice.Display.ShowPaymentInstructions && !string.IsNullOrWhiteSpace(_invoice.PaymentInstructions))
                    column.Item().ShowEntire().Element(c => ComposeNote(c, "PAYMENT INSTRUCTIONS", _invoice.PaymentInstructions!));
            });
        }

        private void ComposeParties(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Spacing(2);
                    column.Item().Text("BILLED TO").FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.08f);
                    column.Item().PaddingTop(2).Text(_invoice.CustomerName ?? "Customer").FontSize(11).Bold();
                    if (!string.IsNullOrWhiteSpace(_invoice.CustomerAddress))
                        column.Item().Text(_invoice.CustomerAddress).FontColor(Secondary);
                    if (_invoice.Display.ShowCustomerEmail && !string.IsNullOrWhiteSpace(_invoice.CustomerEmail))
                        column.Item().Text(_invoice.CustomerEmail).FontColor(Secondary);
                    if (!string.IsNullOrWhiteSpace(_invoice.CustomerPhone))
                        column.Item().Text(_invoice.CustomerPhone).FontColor(Secondary);
                });

                row.ConstantItem(210).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.ConstantColumn(90);
                    });

                    void Fact(string label, string value, string color = Ink)
                    {
                        table.Cell().PaddingVertical(2).AlignRight().Text(label).FontColor(Muted);
                        table.Cell().PaddingVertical(2).AlignRight().Text(value).SemiBold().FontColor(color);
                    }

                    Fact("Invoice date", FormatDate(_invoice.InvoiceDate));
                    if (_invoice.DueDate.HasValue)
                        Fact("Due date", FormatDate(_invoice.DueDate.Value), _invoice.Status == "Overdue" ? "#E11D48" : Ink);
                    if (!string.IsNullOrWhiteSpace(_invoice.ReferenceNo))
                        Fact("Reference / PO", _invoice.ReferenceNo!);
                });
            });
        }

        private void ComposeLines(IContainer container)
        {
            var lines = _invoice.SalesInvoiceLines ?? new List<Dto.Sales.SalesInvoiceLine>();
            var showDiscount = lines.Any(l => (l.Discount ?? 0) != 0);

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);
                    columns.RelativeColumn();
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(80);
                    if (showDiscount)
                        columns.ConstantColumn(44);
                    columns.ConstantColumn(86);
                });

                table.Header(header =>
                {
                    IContainer HeaderCell(IContainer cell) =>
                        cell.Background(Shade).PaddingVertical(7).PaddingHorizontal(6).DefaultTextStyle(s => s.FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.06f));

                    header.Cell().Element(HeaderCell).Text("#");
                    header.Cell().Element(HeaderCell).Text("DESCRIPTION");
                    header.Cell().Element(HeaderCell).AlignRight().Text("QTY");
                    header.Cell().Element(HeaderCell).AlignRight().Text("UNIT PRICE");
                    if (showDiscount)
                        header.Cell().Element(HeaderCell).AlignRight().Text("DISC");
                    header.Cell().Element(HeaderCell).AlignRight().Text("AMOUNT");
                });

                var index = 0;
                foreach (var line in lines)
                {
                    index++;
                    var quantity = line.Quantity ?? 1;
                    var price = line.Amount ?? 0;
                    var discount = line.Discount ?? 0;
                    var total = line.LineTotal ?? Math.Round(quantity * price * (1 - discount / 100m), 2, MidpointRounding.AwayFromZero);

                    IContainer BodyCell(IContainer cell) =>
                        cell.BorderBottom(0.75f).BorderColor(Rule).PaddingVertical(7).PaddingHorizontal(6);

                    table.Cell().Element(BodyCell).Text(index.ToString(CultureInfo.InvariantCulture)).FontColor(Muted);
                    table.Cell().Element(BodyCell).Text(line.ItemDescription ?? "Item");
                    table.Cell().Element(BodyCell).AlignRight().Text(quantity.ToString("0.##", CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(price));
                    if (showDiscount)
                        table.Cell().Element(BodyCell).AlignRight().Text(discount == 0 ? "—" : $"{discount.ToString("0.##", CultureInfo.InvariantCulture)}%");
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(total)).SemiBold();
                }
            });
        }

        private void ComposeTotals(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(230).Background(Shade).CornerRadius(6).Padding(12).Column(column =>
                {
                    column.Spacing(5);

                    void Line(string label, string value, bool strong = false, string color = Ink)
                    {
                        column.Item().Row(r =>
                        {
                            var left = r.RelativeItem().Text(label).FontColor(strong ? Ink : Secondary);
                            var right = r.AutoItem().Text(value).FontColor(color);
                            if (strong)
                            {
                                left.Bold().FontSize(11);
                                right.Bold().FontSize(11);
                            }
                        });
                    }

                    var total = _invoice.TotalAmountAfterTax ?? _invoice.Amount;
                    Line("Subtotal", Money(_invoice.Amount));
                    if ((_invoice.TotalTax ?? 0) > 0)
                        Line("Tax", Money(_invoice.TotalTax!.Value));
                    column.Item().LineHorizontal(0.75f).LineColor(Rule);
                    Line("Total", Money(total), strong: true);
                    if (_invoice.PaidAmount > 0)
                        Line("Amount paid", $"−{Money(_invoice.PaidAmount)}", color: Brand);
                    column.Item().LineHorizontal(0.75f).LineColor(Rule);
                    Line($"Balance due ({_currencyCode})", Money(_invoice.BalanceDue), strong: true, color: Brand);
                });
            });
        }

        private void ComposePayments(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingBottom(6).Text("PAYMENTS RECEIVED").FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.08f);
                foreach (var payment in _invoice.Payments!)
                {
                    column.Item().BorderBottom(0.75f).BorderColor(Rule).PaddingVertical(5).Row(row =>
                    {
                        row.ConstantItem(90).Text(FormatDate(payment.Date)).FontColor(Secondary);
                        row.RelativeItem().Text(string.Join(" · ", new[] { payment.PaymentMethod, payment.ReferenceNo ?? payment.ReceiptNo }
                            .Where(s => !string.IsNullOrWhiteSpace(s)))).FontColor(Secondary);
                        row.AutoItem().Text(Money(payment.Amount)).SemiBold();
                    });
                }
            });
        }

        private void ComposeNote(IContainer container, string heading, string text)
        {
            container.Column(column =>
            {
                column.Item().BorderBottom(0.75f).BorderColor(Rule).PaddingBottom(6)
                    .Text(heading).FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.08f);
                column.Item().PaddingTop(6).Text(text).FontColor(Ink);
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.BorderTop(0.75f).BorderColor(Rule).PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text($"{SellerName} · Invoice #{InvoiceNumber}").FontSize(8).FontColor(Muted);
                row.AutoItem().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }

        private string Money(decimal amount)
        {
            var sign = amount < 0 ? "−" : string.Empty;
            return $"{sign}{_currencySymbol}{Math.Abs(amount).ToString("#,##0.00", CultureInfo.InvariantCulture)}";
        }

        private static string FormatDate(DateTime date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
