#nullable disable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace AccountGo.BackendTests
{
    // Stock posting edge cases: failed postings, zero quantities, voids with repeated items, rounding and negative stock.
    public partial class MultiTenancyPhase3Tests
    {
        // A zero-quantity stock line moves no stock and doesn't stop the invoice from posting.
        [Fact]
        public async Task Invoice_ZeroQuantityStockLine_StillPosts()
        {
            var itemId = CreateStockItem("Zero quantity stock");
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

        // An invoice whose posting fails keeps its stock, however many payments are recorded on it. Posting fails here
        // because the second line's item has no Inventory Adjustments account.
        [Fact]
        public async Task UnpostedInvoice_PaymentsDoNotMoveStock()
        {
            var itemId = CreateStockItem("Unposted stock");
            var brokenId = CreateStockItem("Stock missing accounts");
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
                ItemId = brokenId, ItemDescription = "Stock missing accounts", Quantity = 1m, Amount = 20m, MeasurementId = first.MeasurementId
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
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, pay.StatusCode);
            }
            // Stock may only leave with the invoice's posting; the invoice is still unposted.
            AssertStockState(itemId, 10m, 5m);
            WithDb(db =>
            {
                Assert.Null(db.SalesInvoiceHeaders.Single(i => i.Id == id).GeneralLedgerHeaderId);
                Assert.Empty(db.InventoryControlJournals.Where(j => j.DocumentId == id && j.DocumentType == Core.Domain.DocumentTypes.SalesInvoice));
            });
        }

        // Voiding reverses every line, including repeated lines of the same item, and voiding again changes nothing.
        [Fact]
        public async Task VoidBill_WithTwoLinesOfSameStockItem_RemovesAllStock()
        {
            var itemId = CreateStockItem("Repeated bill stock");
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Repeated stock vendor" });
            var bill = NewBill(vendor.Id, ("Repeated bill stock", 4, 5m, null), ("Repeated bill stock", 6, 5m, null));
            bill.Status = "Open";
            var saved = await SaveBillOk(bill);
            AssertStockState(itemId, 10m, 5m);
            Assert.True((await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={saved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction")).IsSuccessStatusCode);
            AssertStockState(itemId, 0m, 5m);
            AssertStockInvariant(itemId);
            Assert.True((await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={saved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction")).IsSuccessStatusCode);
            AssertStockState(itemId, 0m, 5m);
        }

        [Fact]
        public async Task VoidInvoice_WithTwoLinesOfSameStockItem_RestoresAllStock()
        {
            var itemId = CreateStockItem("Repeated invoice stock");
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
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}&date={IsoDate(DateTime.UtcNow)}")).IsSuccessStatusCode);
            AssertStockState(itemId, 10m, 5m);
            AssertStockInvariant(itemId);
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}&date={IsoDate(DateTime.UtcNow)}")).IsSuccessStatusCode);
            AssertStockState(itemId, 10m, 5m);
        }

        // Selling everything takes the exact stock value, leaving no rounding cents in Inventory.
        [Fact]
        public async Task SellingEverything_LeavesNothingInInventory()
        {
            var itemId = CreateStockItem("Rounding stock");
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
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}&date={IsoDate(DateTime.UtcNow)}")).IsSuccessStatusCode);
            AssertStockInvariant(itemId);
            Assert.True((await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={saved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction")).IsSuccessStatusCode);
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
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, paid.StatusCode);
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

        // Selling more than is in stock, then receiving less than the shortfall, keeps a positive average cost, so the
        // next sale costs a positive amount and the stock value still matches Inventory.
        [Fact]
        public async Task ReceiptIntoNegativeStock_KeepsAPositiveAverageCost()
        {
            var itemId = CreateStockItem("Oversold stock");
            AdjustStock(itemId, 1m, 10m, "Count", "Seed");
            var oversold = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", await NewStockInvoice(itemId, 6m));
            Assert.True(oversold.IsSuccessStatusCode, await oversold.Content.ReadAsStringAsync());
            AssertStockState(itemId, -5m, 10m);

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Oversold stock vendor" });
            var bill = NewBill(vendor.Id, ("Oversold stock", 2, 100m, null));
            bill.Status = "Open";
            await SaveBillOk(bill);
            AssertStockState(itemId, -3m, 100m);
            AssertStockInvariant(itemId);

            var next = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", await NewStockInvoice(itemId, 1m));
            Assert.True(next.IsSuccessStatusCode, await next.Content.ReadAsStringAsync());
            var id = (await Json(next)).GetProperty("id").GetInt32();
            WithDb(db =>
            {
                var invoice = db.SalesInvoiceHeaders.Single(i => i.Id == id);
                Assert.NotNull(invoice.GeneralLedgerHeaderId);
                Assert.Contains(db.GeneralLedgerLines.Where(l => l.GeneralLedgerHeaderId == invoice.GeneralLedgerHeaderId).ToList(),
                    line => line.AccountId == AccountIdIn(db, "50300") && line.DrCr == Core.Domain.DrOrCrSide.Dr && line.Amount == 100m);
            });
            AssertStockState(itemId, -4m, 100m);
            AssertStockInvariant(itemId);
        }
    }
}
