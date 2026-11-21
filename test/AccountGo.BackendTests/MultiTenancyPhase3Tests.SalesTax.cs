#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Schema;
using Api.Service;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests
{
    public abstract partial class MultiTenancyPhase3TestBase
    {

        protected int AddSalesTax(decimal rate, int? accountId = null)
        {
            var id = 0;
            WithDb(db =>
            {
                var tax = new Tax
                {
                    TaxCode = "SALE-" + rate.ToString(CultureInfo.InvariantCulture),
                    TaxName = "Sales tax " + rate.ToString(CultureInfo.InvariantCulture),
                    Rate = rate, IsActive = true,
                    SalesAccountId = accountId ?? AccountIdIn(db, "20300")
                };
                db.Taxes.Add(tax);
                db.SaveChanges();
                id = tax.Id;
            });
            return id;
        }

        protected async Task<Dto.Sales.SalesInvoice> NewTaxInvoice()
        {
            var invoice = await NewInvoice();
            WithDb(db =>
            {
                var item = db.Items.Single(i => i.Id == invoice.SalesInvoiceLines.Single().ItemId);
                item.ItemCategory = new Core.Domain.Items.ItemCategory
                {
                    Name = "Sales tax services", ItemType = ItemTypes.Service,
                    MeasurementId = item.SmallestMeasurementId.Value, SalesAccountId = item.SalesAccountId
                };
                db.SaveChanges();
            });
            return invoice;
        }

        protected async Task<Dto.Sales.SalesInvoice> SaveTaxInvoice(Dto.Sales.SalesInvoice invoice)
        {
            var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await ReadDto<Dto.Sales.SalesInvoice>(response);
        }

        protected int SalesTaxLedgerId(int invoiceId)
        {
            var id = 0;
            WithDb(db => id = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId).GeneralLedgerHeaderId.Value);
            return id;
        }

        protected static void AssertTaxTotals(Dto.Sales.SalesInvoice invoice, decimal subtotal, decimal tax, decimal total)
        {
            Assert.Equal(subtotal, invoice.Amount);
            Assert.Equal(tax, invoice.TotalTax);
            Assert.Equal(total, invoice.TotalAmountAfterTax);
        }

        protected static void AssertBalancedSalesTax(List<GeneralLedgerLine> entry) =>
            Assert.Equal(entry.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount),
                entry.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount));
    }

    public sealed class MultiTenancyPhase3SalesTaxTests : MultiTenancyPhase3TestBase
    {
        public MultiTenancyPhase3SalesTaxTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(100, 1, 0, 100, 12, 112)]
        [InlineData(200, 1, 10, 180, 21.60, 201.60)]
        [InlineData(0.05, 3, 10, 0.14, 0.02, 0.16)]
        [InlineData(2.30, 1, 5, 2.19, 0.26, 2.45)]
        public async Task SalesTax_SaveReadAndIssue_UseRoundedExclusiveLineTax(
            decimal price, decimal quantity, decimal discount, decimal subtotal, decimal taxAmount, decimal total)
        {
            var taxId = AddSalesTax(12m);
            var invoice = await NewTaxInvoice();
            invoice.Status = "Draft";
            invoice.InvoiceDate = DateTime.ParseExact(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var line = invoice.SalesInvoiceLines.Single();
            line.Amount = price;
            line.Quantity = quantity;
            line.Discount = discount;
            line.TaxId = taxId;
            line.TaxAmount = 999m;
            line.TaxRate = 99m;

            var saved = await SaveTaxInvoice(invoice);
            AssertTaxTotals(saved, subtotal, taxAmount, total);
            Assert.Equal(taxId, saved.SalesInvoiceLines.Single().TaxId);
            Assert.Equal(taxAmount, saved.SalesInvoiceLines.Single().TaxAmount);

            var reopened = await GetInvoice(saved.Id);
            AssertTaxTotals(reopened, subtotal, taxAmount, total);
            Assert.Equal(taxId, reopened.SalesInvoiceLines.Single().TaxId);
            reopened.SalesInvoiceLines.Single().TaxAmount = 1000m;
            var resaved = await SaveTaxInvoice(reopened);
            AssertTaxTotals(resaved, subtotal, taxAmount, total);
            Assert.Equal(reopened.SalesInvoiceLines.Single().Id, resaved.SalesInvoiceLines.Single().Id);

            var issued = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={PubId<SalesInvoiceHeader>(saved.Id)}");
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            var entry = Ledger(SalesTaxLedgerId(saved.Id));
            Assert.Equal(total, Assert.Single(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("10120")).Amount);
            // Sales post before discount; the discount goes to Sales Discounts.
            var gross = Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero);
            Assert.Equal(gross, Assert.Single(entry, l => l.DrCr == DrOrCrSide.Cr && l.AccountId == AccountId("40100")).Amount);
            if (gross != subtotal)
                Assert.Equal(gross - subtotal, Assert.Single(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("40400")).Amount);
            else
                Assert.DoesNotContain(entry, l => l.AccountId == AccountId("40400"));
            Assert.Equal(taxAmount, Assert.Single(entry, l => l.DrCr == DrOrCrSide.Cr && l.AccountId == AccountId("20300")).Amount);
            AssertBalancedSalesTax(entry);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SalesTax_FullPayment_ClearsDocumentAndReceivables(bool payInParts)
        {
            var invoice = await NewTaxInvoice();
            invoice.Status = "Open";
            invoice.SalesInvoiceLines.Single().TaxId = AddSalesTax(12m);
            var saved = await SaveTaxInvoice(invoice);
            Assert.True(saved.Posted);

            if (payInParts)
            {
                var partial = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                    new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = saved.Id, Amount = 100m });
                Assert.True(partial.IsSuccessStatusCode, await partial.Content.ReadAsStringAsync());
                Assert.False((await Json(partial)).GetProperty("isFullyPaid").GetBoolean());
                var partiallyPaid = await GetInvoice(saved.Id);
                Assert.Equal("Partially Paid", partiallyPaid.Status);
                Assert.Equal(12m, partiallyPaid.BalanceDue);
            }

            var final = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = saved.Id, Amount = payInParts ? 12m : 112m });
            Assert.True(final.IsSuccessStatusCode, await final.Content.ReadAsStringAsync());
            Assert.True((await Json(final)).GetProperty("isFullyPaid").GetBoolean());
            var paid = await GetInvoice(saved.Id);
            Assert.Equal("Paid", paid.Status);
            Assert.Equal(112m, paid.PaidAmount);
            Assert.Equal(0m, paid.BalanceDue);
            WithDb(db =>
            {
                var ar = AccountIdIn(db, "10120");
                var receiptIds = db.CustomerAllocations.Where(a => a.SalesInvoiceHeaderId == saved.Id)
                    .Select(a => a.SalesReceiptHeaderId).ToArray();
                var receiptLedgers = db.SalesReceiptHeaders.Where(r => receiptIds.Contains(r.Id))
                    .Select(r => r.GeneralLedgerHeaderId).ToArray();
                var invoiceLedger = db.SalesInvoiceHeaders.Single(i => i.Id == saved.Id).GeneralLedgerHeaderId;
                var entries = db.GeneralLedgerLines.Where(l => l.AccountId == ar &&
                    (l.GeneralLedgerHeaderId == invoiceLedger || receiptLedgers.Contains(l.GeneralLedgerHeaderId))).ToList();
                Assert.Equal(0m, entries.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount));
            });
        }

        [Fact]
        public async Task SalesTax_AlternateCreateAndUpdate_RecalculateChosenTaxAndReturnTotals()
        {
            var invoice = await NewTaxInvoice();
            invoice.Status = "Draft";
            invoice.SalesInvoiceLines.Single().TaxId = AddSalesTax(12m);
            invoice.SalesInvoiceLines.Single().TaxAmount = 999m;
            var createdResponse = await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice);
            Assert.True(createdResponse.IsSuccessStatusCode, await createdResponse.Content.ReadAsStringAsync());
            var created = await ReadDto<Dto.Sales.SalesInvoice>(createdResponse);
            AssertTaxTotals(created, 100, 12, 112);
            var line = created.SalesInvoiceLines.Single();
            line.Quantity = 2;
            line.TaxId = AddSalesTax(5m);
            line.TaxAmount = 999m;
            var updatedResponse = await Send(HttpMethod.Post, "/api/Sales/UpdateSalesInvoice", created);
            Assert.True(updatedResponse.IsSuccessStatusCode, await updatedResponse.Content.ReadAsStringAsync());
            var updated = await ReadDto<Dto.Sales.SalesInvoice>(updatedResponse);
            AssertTaxTotals(updated, 200, 10, 210);
            Assert.Equal(line.TaxId, updated.SalesInvoiceLines.Single().TaxId);
            updated.SalesInvoiceLines.Single().TaxId = null;
            var untaxed = await SaveTaxInvoice(updated);
            AssertTaxTotals(untaxed, 200, 0, 200);
        }

        [Fact]
        public async Task SalesTax_DifferentTaxesAndShipping_PostToTheirAccounts()
        {
            var invoice = await NewTaxInvoice();
            invoice.Status = "Open";
            invoice.ShippingHandlingCharge = 10m;
            var line = invoice.SalesInvoiceLines.Single();
            line.TaxId = AddSalesTax(12m);
            invoice.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine
            {
                ItemId = line.ItemId, MeasurementId = line.MeasurementId, Quantity = 1, Amount = 200,
                TaxId = AddSalesTax(5m, AccountId("20110"))
            });
            var saved = await SaveTaxInvoice(invoice);
            AssertTaxTotals(saved, 300m, 22m, 332m);
            var entry = Ledger(SalesTaxLedgerId(saved.Id));
            Assert.Contains(entry, l => l.AccountId == AccountId("20300") && l.DrCr == DrOrCrSide.Cr && l.Amount == 12m);
            Assert.Contains(entry, l => l.AccountId == AccountId("20110") && l.DrCr == DrOrCrSide.Cr && l.Amount == 10m);
            Assert.Contains(entry, l => l.AccountId == AccountId("10120") && l.DrCr == DrOrCrSide.Dr && l.Amount == 332m);
            AssertBalancedSalesTax(entry);
        }

        [Fact]
        public async Task SalesTax_NoSelectedTax_IgnoresItemCustomerIntersection()
        {
            var invoice = await NewTaxInvoice();
            invoice.Status = "Open";
            var taxId = AddSalesTax(12m);
            WithDb(db =>
            {
                var itemGroup = new ItemTaxGroup { Name = "Taxed items" };
                itemGroup.ItemTaxGroupTax.Add(new ItemTaxGroupTax { TaxId = taxId });
                var customerGroup = new TaxGroup { Description = "Taxed customers", IsActive = true };
                customerGroup.TaxGroupTax.Add(new TaxGroupTax { TaxId = taxId });
                db.Items.Single(i => i.Id == invoice.SalesInvoiceLines.Single().ItemId).ItemTaxGroup = itemGroup;
                db.Customers.Single(c => c.Id == invoice.CustomerId).TaxGroup = customerGroup;
                db.SaveChanges();
            });
            var saved = await SaveTaxInvoice(invoice);
            AssertTaxTotals(saved, 100m, 0m, 100m);
            Assert.Null(saved.SalesInvoiceLines.Single().TaxId);
            var entry = Ledger(SalesTaxLedgerId(saved.Id));
            Assert.Equal(2, entry.Count);
            Assert.DoesNotContain(entry, l => l.AccountId == AccountId("20300"));
            AssertBalancedSalesTax(entry);
        }

        [Fact]
        public async Task SalesTax_MissingSalesAccount_ReportsErrorWithoutPostingOrMovingStock()
        {
            var itemId = CreateStockItem("Tax account validation stock");
            AdjustStock(itemId, 10, 5, "Count", "Opening stock");
            var invoice = await NewStockInvoice(itemId, 3);
            var taxId = AddSalesTax(12m);
            WithDb(db =>
            {
                db.Taxes.Single(t => t.Id == taxId).SalesAccountId = null;
                db.SaveChanges();
            });
            var first = invoice.SalesInvoiceLines.Single();
            invoice.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine
            {
                ItemId = first.ItemId, MeasurementId = first.MeasurementId, Quantity = 1, Amount = 20, TaxId = taxId
            });
            var saved = await SaveTaxInvoice(invoice);
            Assert.False(saved.Posted);
            Assert.Equal("Tax Sales tax 12 has no sales tax account", saved.PostingError);
            Assert.DoesNotContain(saved.Warnings, warning => warning.Contains("sales tax account", StringComparison.Ordinal));
            var posted = await Send(HttpMethod.Post, "/api/Sales/PostSalesInvoice", saved);
            Assert.Equal(HttpStatusCode.BadRequest, posted.StatusCode);
            Assert.Contains("Tax Sales tax 12 has no sales tax account", await posted.Content.ReadAsStringAsync());
            var issued = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={PubId<SalesInvoiceHeader>(saved.Id)}");
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            var issuedJson = await Json(issued);
            Assert.Equal("Tax Sales tax 12 has no sales tax account", issuedJson.GetProperty("postingError").GetString());
            Assert.DoesNotContain(issuedJson.GetProperty("warnings").EnumerateArray(),
                warning => warning.GetString()!.Contains("sales tax account", StringComparison.Ordinal));
            AssertStockState(itemId, 10, 5);
            WithDb(db =>
            {
                Assert.Null(db.SalesInvoiceHeaders.Single(i => i.Id == saved.Id).GeneralLedgerHeaderId);
                Assert.Empty(db.InventoryControlJournals.Where(j => j.DocumentType == DocumentTypes.SalesInvoice && j.DocumentId == saved.Id));
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SalesTax_ForeignTax_IsRejectedOnCreateAndEdit(bool editing)
        {
            var invoice = await NewTaxInvoice();
            invoice.Status = "Draft";
            if (editing)
                invoice = await SaveTaxInvoice(invoice);
            var org = await Send(HttpMethod.Post, "/api/organizations", new { name = "Foreign tax", slug = "foreign-tax" }, scoped: false);
            Assert.Equal(HttpStatusCode.Created, org.StatusCode);
            var companyId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.ResolveCompanyId(_factory.Services, (await Json(org)).GetProperty("id").GetString());
            var taxId = 0;
            string taxPub = null;
            using (var scope = _factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(companyId, "foreign-tax", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var t = db.Taxes.First();
                taxId = t.Id;
                taxPub = t.PublicId;
            }
            var line = invoice.SalesInvoiceLines.Single();
            var payload = new
            {
                id = editing ? PubId<SalesInvoiceHeader>(invoice.Id) : null,
                customerId = PubId<Customer>(invoice.CustomerId),
                invoiceDate = invoice.InvoiceDate,
                status = "Draft",
                salesInvoiceLines = new[]
                {
                    new
                    {
                        itemId = line.ItemId.HasValue ? PubId<Core.Domain.Items.Item>(line.ItemId.Value) : null,
                        measurementId = line.MeasurementId.HasValue ? PubId<Core.Domain.Items.Measurement>(line.MeasurementId.Value) : null,
                        quantity = line.Quantity,
                        amount = line.Amount,
                        taxId = taxPub
                    }
                }
            };
            var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", payload);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("unknown_id", (await Json(response)).GetProperty("code").GetString());
            WithDb(db => Assert.DoesNotContain(db.SalesInvoiceLines, l => l.TaxId == taxId));
        }

        [Fact]
        public async Task SalesTax_IssuedTax_IsStableAcrossViewsPrintingAndTaxSettingChanges()
        {
            var invoice = await NewTaxInvoice();
            invoice.Status = "Open";
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemDescription = "Taxed consulting";
            line.TaxId = AddSalesTax(12m);
            var saved = await SaveTaxInvoice(invoice);
            WithDb(db =>
            {
                var tax = db.Taxes.Single(t => t.Id == line.TaxId);
                tax.Rate = 99m;
                tax.IsActive = false;
                db.SaveChanges();
            });
            AssertTaxTotals(await GetInvoice(saved.Id), 100, 12, 112);
            foreach (var route in new[] { "/api/Sales/SalesInvoices", $"/api/Sales/CustomerInvoices?id={PubId<Customer>(invoice.CustomerId)}" })
            {
                var response = await Send(HttpMethod.Get, route);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                var list = await ReadDto<List<Dto.Sales.SalesInvoice>>(response);
                AssertTaxTotals(list.Single(i => i.Id == saved.Id), 100, 12, 112);
            }
            var printing = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoiceForPrinting?id={PubId<SalesInvoiceHeader>(saved.Id)}");
            Assert.True(printing.IsSuccessStatusCode, await printing.Content.ReadAsStringAsync());
            var printed = await ReadDto<Dto.Sales.SalesInvoice>(printing);
            AssertTaxTotals(printed, 100, 12, 112);
            Assert.Equal(12m, printed.SalesInvoiceLines.Single().TaxAmount);
            var email = InvoiceEmailTemplate.Build(printed, "USD", canReply: true);
            Assert.Contains("Invoice 1 from Phase Three", email.Subject);
            // The PDF carries the lines; the email gives the totals once.
            Assert.DoesNotContain("Taxed consulting", email.Text);
            Assert.Contains("Amount due: $112.00", email.Text);
            Assert.Contains("$112.00", email.Html);
            var pdf = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoicePdf?id={PubId<SalesInvoiceHeader>(saved.Id)}");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType.MediaType);
        }

        [Fact]
        public async Task SalesTax_Void_ReversesReceivablesRevenueTaxAndCogs()
        {
            var itemId = CreateStockItem("Taxed COGS stock");
            AdjustStock(itemId, 10, 5, "Count", "Opening stock");
            var invoice = await NewStockInvoice(itemId, 5);
            invoice.SalesInvoiceLines.Single().TaxId = AddSalesTax(12m);
            var saved = await SaveTaxInvoice(invoice);
            AssertTaxTotals(saved, 100, 12, 112);
            var entry = Ledger(SalesTaxLedgerId(saved.Id));
            Assert.Contains(entry, l => l.AccountId == AccountId("50300") && l.DrCr == DrOrCrSide.Dr && l.Amount == 25m);
            Assert.Contains(entry, l => l.AccountId == AccountId("10800") && l.DrCr == DrOrCrSide.Cr && l.Amount == 25m);
            AssertBalancedSalesTax(entry);
            AssertStockState(itemId, 5, 5);
            var response = await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={PubId<SalesInvoiceHeader>(saved.Id)}&date={IsoDate(DateTime.UtcNow)}");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var both = entry.Concat(Ledger(InvoiceVoidLedgerId(saved.Id))).ToList();
            Assert.All(both.Select(l => l.AccountId).Distinct(), account =>
                Assert.Equal(0m, both.Where(l => l.AccountId == account).Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount)));
            AssertStockState(itemId, 10, 5);
            Assert.Equal(0m, (await GetInvoice(saved.Id)).BalanceDue);
        }

        [SqliteFact]
        public async Task SalesTax_SchemaUpgrade_DefaultsExistingDraftAndIssuedLinesWithoutChangingLedger()
        {
            foreach (var status in new[] { "Draft", "Open" })
            {
                var invoice = await NewTaxInvoice();
                invoice.Status = status;
                await SaveTaxInvoice(invoice);
            }
            using var source = new SqliteConnection("Data Source=" + _path);
            using var replica = new SqliteConnection("Data Source=:memory:");
            source.Open();
            replica.Open();
            source.BackupDatabase(replica);
            SQLitePCL.raw.sqlite3_limit(replica.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);
            using var command = replica.CreateCommand();
            command.CommandText = @"SELECT SUM(""Amount"") FROM ""GeneralLedgerLine""";
            var ledgerBefore = command.ExecuteScalar();
            command.CommandText = @"SELECT COUNT(*) FROM ""SalesInvoiceLine""";
            var lineCountBefore = command.ExecuteScalar();
            command.CommandText = @"PRAGMA table_info(""SalesInvoiceLine"")";
            var columns = new List<string>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (reader.GetString(1) is not ("TaxId" or "TaxAmount"))
                        columns.Add("\"" + reader.GetString(1) + "\"");
            command.CommandText = $@"
                PRAGMA foreign_keys=OFF;
                CREATE TABLE ""SalesLineBeforeTax"" AS SELECT {string.Join(", ", columns)} FROM ""SalesInvoiceLine"";
                DROP TABLE ""SalesInvoiceLine"";
                ALTER TABLE ""SalesLineBeforeTax"" RENAME TO ""SalesInvoiceLine"";
                PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();

            SqliteSchemaUpgrader.Upgrade(replica);
            SqliteSchemaUpgrader.Upgrade(replica);
            command.CommandText = @"SELECT COUNT(*) FROM ""SalesInvoiceLine"" WHERE ""TaxId"" IS NOT NULL OR ""TaxAmount"" != 0";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = @"SELECT COUNT(*) FROM ""SalesInvoiceLine""";
            Assert.Equal(lineCountBefore, command.ExecuteScalar());
            command.CommandText = @"SELECT SUM(""Amount"") FROM ""GeneralLedgerLine""";
            Assert.Equal(ledgerBefore, command.ExecuteScalar());
            command.CommandText = @"UPDATE ""SalesInvoiceLine"" SET ""TaxId"" = 2147483647";
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
            Assert.True(ColumnExists(command, "SalesInvoiceLine", "TaxAmount"));
        }
    }
}
