#nullable disable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace AccountGo.BackendTests
{
    // Regression cases from the stock tracking review.
    public partial class MultiTenancyPhase3Tests
    {
        // Issue 2: a zero-quantity stock line makes posting throw, so the issued invoice is left unposted.
        [Fact]
        public async Task Probe_ZeroQuantityStockLine_InvoiceIsStillPosted()
        {
            var itemId = CreateStockItem("Probe zero qty");
            AdjustStock(itemId, 10m, 5m, "Count", "seed");
            var invoice = await NewStockInvoice(itemId, 3m);
            var first = invoice.SalesInvoiceLines.Single();
            invoice.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine
            {
                ItemId = first.ItemId, ItemDescription = first.ItemDescription, Quantity = 0m, Amount = 20m, MeasurementId = first.MeasurementId
            });
            var res = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var id = (await Json(res)).GetProperty("id").GetInt32();
            WithDb(db => Assert.NotNull(db.SalesInvoiceHeaders.Single(i => i.Id == id).GeneralLedgerHeaderId));
        }

        // Issue 1: paying an invoice whose posting failed saves its stock issue, again on every payment.
        // Posting fails here because the second line's item has no Inventory Adjustments account (any failure after
        // the first line's stock issue does the same).
        [Fact]
        public async Task Probe_UnpostedInvoice_PaymentsMustNotMoveStock()
        {
            var itemId = CreateStockItem("Probe unposted");
            var brokenId = CreateStockItem("Probe broken accounts");
            WithDb(db =>
            {
                db.Items.Single(i => i.Id == brokenId).InventoryAdjustmentAccountId = null;
                db.SaveChanges();
            });
            AdjustStock(itemId, 10m, 5m, "Count", "seed");
            var invoice = await NewStockInvoice(itemId, 3m);
            var first = invoice.SalesInvoiceLines.Single();
            invoice.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine
            {
                ItemId = brokenId, ItemDescription = "Probe broken accounts", Quantity = 1m, Amount = 20m, MeasurementId = first.MeasurementId
            });
            var res = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var id = (await Json(res)).GetProperty("id").GetInt32();
            WithDb(db => Assert.Null(db.SalesInvoiceHeaders.Single(i => i.Id == id).GeneralLedgerHeaderId));
            AssertStockState(itemId, 10m, 5m);

            for (var i = 0; i < 2; i++)
            {
                var pay = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                    new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = id, Amount = 10, PaymentDate = DateTime.UtcNow });
                Assert.True(pay.IsSuccessStatusCode, await pay.Content.ReadAsStringAsync());
            }
            // Stock may only leave with the invoice's posting; the invoice is still unposted.
            AssertStockState(itemId, 10m, 5m);
            WithDb(db =>
            {
                Assert.Null(db.SalesInvoiceHeaders.Single(i => i.Id == id).GeneralLedgerHeaderId);
                Assert.Empty(db.InventoryControlJournals.Where(j => j.DocumentId == id && j.DocumentType == Core.Domain.DocumentTypes.SalesInvoice));
            });
        }

        // Issue 3: voiding a bill with two lines of the same item reverses only the first line.
        [Fact]
        public async Task Probe_VoidBill_WithTwoLinesOfSameStockItem_RemovesAllStock()
        {
            var itemId = CreateStockItem("Probe dup bill");
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Probe vendor 1" });
            var bill = NewBill(vendor.Id, ("Probe dup bill", 4, 5m, null), ("Probe dup bill", 6, 5m, null));
            bill.Status = "Open";
            var saved = await SaveBillOk(bill);
            AssertStockState(itemId, 10m, 5m);
            Assert.True((await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={saved.Id}")).IsSuccessStatusCode);
            AssertStockState(itemId, 0m, 5m);
            AssertStockInvariant(itemId);
            Assert.True((await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={saved.Id}")).IsSuccessStatusCode);
            AssertStockState(itemId, 0m, 5m);
        }

        // Issue 3, invoice side.
        [Fact]
        public async Task Probe_VoidInvoice_WithTwoLinesOfSameStockItem_RestoresAllStock()
        {
            var itemId = CreateStockItem("Probe dup inv");
            AdjustStock(itemId, 10m, 5m, "Count", "seed");
            var invoice = await NewStockInvoice(itemId, 2m);
            var first = invoice.SalesInvoiceLines.Single();
            invoice.SalesInvoiceLines.Add(new Dto.Sales.SalesInvoiceLine
            {
                ItemId = first.ItemId, ItemDescription = first.ItemDescription, Quantity = 3m, Amount = 20m, MeasurementId = first.MeasurementId
            });
            var res = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var id = (await Json(res)).GetProperty("id").GetInt32();
            AssertStockState(itemId, 5m, 5m);
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}")).IsSuccessStatusCode);
            AssertStockState(itemId, 10m, 5m);
            AssertStockInvariant(itemId);
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}")).IsSuccessStatusCode);
            AssertStockState(itemId, 10m, 5m);
        }

        // Issue 4: selling all stock leaves rounding pennies in the Inventory account.
        [Fact]
        public async Task Probe_SellingEverything_LeavesNothingInInventory()
        {
            var itemId = CreateStockItem("Probe pennies");
            AdjustStock(itemId, 1m, 1.00m, "Count", "a");
            AdjustStock(itemId, 2m, 1.01m, "Count", "b"); // 3.02 in stock, average 1.0067
            for (var i = 0; i < 3; i++)
            {
                var res = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", await NewStockInvoice(itemId, 1m));
                Assert.True(res.IsSuccessStatusCode);
            }
            WithDb(db =>
            {
                var value = db.InventoryControlJournals.Where(j => j.ItemId == itemId).ToList()
                    .Sum(j => (j.INQty.HasValue ? 1 : -1) * j.TotalCost.GetValueOrDefault());
                Assert.Equal(0m, value);
                Assert.Equal(0m, db.Items.Single(i => i.Id == itemId).StockValue);
            });
            AssertStockInvariant(itemId);
        }

        [Fact]
        public async Task StockReceipt_DiscountedBill_PreservesExactExtendedCost()
        {
            var itemId = CreateStockItem("Discounted stock");
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Discounted stock vendor" });
            var bill = NewBill(vendor.Id, ("Discounted stock", 30000m, 1.01m, AccountId("10800")));
            bill.Lines[0].ItemId = itemId;
            bill.Lines[0].Discount = 33.33m;
            bill.Status = "Open";
            var saved = await SaveBillOk(bill);
            AssertStockInvariant(itemId);
            WithDb(db => Assert.Equal(20201.01m, db.Items.Single(i => i.Id == itemId).StockValue));
            var invoice = await NewStockInvoice(itemId, 30000m);
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            WithDb(db => Assert.Equal(0m, db.Items.Single(i => i.Id == itemId).StockValue));
            AssertStockInvariant(itemId);
            var id = (await Json(issued)).GetProperty("id").GetInt32();
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}")).IsSuccessStatusCode);
            AssertStockInvariant(itemId);
            Assert.True((await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={saved.Id}")).IsSuccessStatusCode);
            AssertStockState(itemId, 0m, 0.6734m);
            AssertStockInvariant(itemId);
        }

        [Fact]
        public async Task StockPosting_FinalLedgerValidationFailure_DoesNotIssueStock_AndRetryIssuesOnce()
        {
            var itemId = CreateStockItem("Invalid COGS account");
            AdjustStock(itemId, 10m, 5m, "Count", "Seed");
            WithDb(db =>
            {
                db.Items.Single(i => i.Id == itemId).CostOfGoodsSoldAccountId = AccountIdIn(db, "50000");
                db.SaveChanges();
            });
            var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", await NewStockInvoice(itemId, 3m));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var id = (await Json(response)).GetProperty("id").GetInt32();
            for (var payment = 0; payment < 2; payment++)
            {
                var paid = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                    new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = id, Amount = 1m, PaymentDate = DateTime.UtcNow });
                Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
                Assert.DoesNotContain(AllSyncStatements(paid), sql => sql.Contains("INSERT INTO \"InventoryControlJournal\"", StringComparison.Ordinal));
            }
            AssertStockState(itemId, 10m, 5m);
            WithDb(db =>
            {
                Assert.Null(db.SalesInvoiceHeaders.Single(i => i.Id == id).GeneralLedgerHeaderId);
                db.Items.Single(i => i.Id == itemId).CostOfGoodsSoldAccountId = AccountIdIn(db, "50300");
                db.SaveChanges();
            });
            for (var payment = 0; payment < 2; payment++)
            {
                var paid = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                    new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = id, Amount = 1m, PaymentDate = DateTime.UtcNow });
                Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
                AssertStockState(itemId, 7m, 5m);
            }
            AssertStockInvariant(itemId);
        }
    }
}
