#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Domain;
using Core.Domain.Purchases;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Bill_Payment_DebitsPayableAccount_EvenWithNegativeLineLargerThanTotal()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Split Line Vendor" });
            var apAccount = AccountId("20110");
            var expense1 = AccountId("50200");
            var expense2 = AccountId("50300");

            var bill = NewBill(vendor.Id, ("Materials", 1, 1000m, expense1), ("Credit adjustment", 1, -900m, expense2));
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            Assert.Equal(100m, approved.Total);
            Assert.Equal(100m, approved.BalanceDue);

            var payDate = DateTime.UtcNow;
            var paid = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new Dto.Purchasing.PayBill
            {
                BillId = approved.Id,
                Amount = 100m,
                Date = payDate,
                PaymentMethod = "Bank Transfer",
            }));

            Assert.Equal("Paid", paid.Status);
            Assert.Equal(0m, paid.BalanceDue);
            var payment = Assert.Single(paid.Payments);

            var paymentEntry = Ledger(PaymentLedgerId(payment.Id));
            // Payment MUST debit Accounts Payable (20110), NOT the credit adjustment line account (50300)
            Assert.Contains(paymentEntry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == apAccount && l.Amount == 100m);
            Assert.DoesNotContain(paymentEntry, l => l.AccountId == expense2);
        }

        [Fact]
        public async Task Bill_VoidPayment_ReopensBill_AndReversesGL()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Reopening Supplier" });
            var bill = NewBill(vendor.Id, ("Consulting services", 1, 250m, AccountId("50200")));
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);

            var payDate = DateTime.UtcNow;
            var paid = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new Dto.Purchasing.PayBill
            {
                BillId = approved.Id,
                Amount = 250m,
                Date = payDate,
            }));
            Assert.Equal("Paid", paid.Status);
            Assert.Equal(0m, paid.BalanceDue);
            var payment = Assert.Single(paid.Payments);
            Assert.False(payment.IsVoid);

            // Void payment
            var voidDate = payDate.AddMinutes(5);
            var voidRes = await Send(HttpMethod.Post, "/api/Purchasing/VoidPayment", new Dto.Purchasing.VoidPaymentDto
            {
                Id = payment.Id,
                Date = voidDate,
                Reason = "Payment returned unpaid by bank",
            });
            Assert.Equal(HttpStatusCode.OK, voidRes.StatusCode);
            var voidedBill = await BillOk(voidRes);

            // Bill is reopened to Open
            Assert.Equal("Open", voidedBill.Status);
            Assert.Equal(250m, voidedBill.BalanceDue);
            Assert.Equal(0m, voidedBill.AmountPaid);

            // Payment is marked void with audit
            var voidPayment = Assert.Single(voidedBill.Payments);
            Assert.True(voidPayment.IsVoid);
            Assert.Equal("Payment returned unpaid by bank", voidPayment.VoidReason);
            Assert.NotNull(voidPayment.VoidedAt);

            // GL reversal created: DR Cash/Bank, CR Accounts Payable
            int? voidGlId = null;
            WithDb(db =>
            {
                var vp = db.VendorPayments.Single(p => p.Id == payment.Id);
                voidGlId = vp.VoidGeneralLedgerHeaderId;
            });
            Assert.NotNull(voidGlId);
            var reversal = Ledger(voidGlId.Value);
            Assert.Contains(reversal, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == payment.AccountId && l.Amount == 250m);
            Assert.Contains(reversal, l => l.DrCr == DrOrCrSide.Cr && l.AccountId == AccountId("20110") && l.Amount == 250m);
        }

        [Fact]
        public async Task Bill_VoidBill_RequiresAllPaymentsVoid()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Payment Gate Supplier" });
            var bill = NewBill(vendor.Id, ("Equipment maintenance", 1, 500m, AccountId("50200")));
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);

            var payDate = DateTime.UtcNow;
            var paid = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new Dto.Purchasing.PayBill
            {
                BillId = approved.Id,
                Amount = 200m,
                Date = payDate,
            }));
            var payment = Assert.Single(paid.Payments);

            // Attempting to void bill while payment exists is refused
            var voidBillRes = await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction");
            Assert.Equal(HttpStatusCode.BadRequest, voidBillRes.StatusCode);
            var err = await voidBillRes.Content.ReadAsStringAsync();
            Assert.Contains("payments that aren't void", err);

            // Void the payment
            var voidPayRes = await Send(HttpMethod.Post, "/api/Purchasing/VoidPayment", new Dto.Purchasing.VoidPaymentDto
            {
                Id = payment.Id,
                Date = DateTime.UtcNow,
                Reason = "Erroneous payment",
            });
            Assert.Equal(HttpStatusCode.OK, voidPayRes.StatusCode);

            // Now voiding the bill succeeds
            var voidBillOk = await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction"));
            Assert.Equal("Void", voidBillOk.Status);
            Assert.Equal(0m, voidBillOk.BalanceDue);
        }

        [Fact]
        public async Task Bill_VoidDates_Validated()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Date Validation Vendor" });
            var today = DateTime.UtcNow.Date;
            var bill = NewBill(vendor.Id, ("Supplies", 1, 100m, AccountId("50200")));
            bill.Date = today;
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);

            // Void date earlier than bill date
            var yesterday = today.AddDays(-1);
            var voidRes = await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(yesterday)}&reason=correction");
            Assert.Equal(HttpStatusCode.BadRequest, voidRes.StatusCode);
            var msg = await voidRes.Content.ReadAsStringAsync();
            Assert.Contains("cannot be earlier than the", msg);

            // Pay bill today
            var paid = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new Dto.Purchasing.PayBill
            {
                BillId = approved.Id,
                Amount = 100m,
                Date = today,
            }));
            var payment = Assert.Single(paid.Payments);

            // Void payment with date earlier than payment date
            var voidPayRes = await Send(HttpMethod.Post, "/api/Purchasing/VoidPayment", new Dto.Purchasing.VoidPaymentDto
            {
                Id = payment.Id,
                Date = yesterday,
                Reason = "Invalid date test",
            });
            Assert.Equal(HttpStatusCode.BadRequest, voidPayRes.StatusCode);
            var payMsg = await voidPayRes.Content.ReadAsStringAsync();
            Assert.Contains("cannot be earlier than the", payMsg);
        }

        [Fact]
        public async Task Bill_VoidStockBill_RejectedWhenStockSold()
        {
            var itemId = CreateStockItem("Void Guard Item");
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Stock Void Vendor" });

            // Receive 5 units
            var bill = NewBill(vendor.Id, ("Stock line", 5, 10m, AccountId("10800")));
            bill.Lines[0].ItemId = itemId;
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            AssertStockState(itemId, 5m, 10m);

            // Sell 3 units on sales invoice
            var invoice = await NewStockInvoice(itemId, 3m);
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issued.IsSuccessStatusCode);
            AssertStockState(itemId, 2m, 10m);

            // Attempting to void the bill must fail because 3 units are already sold
            var voidRes = await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction");
            Assert.Equal(HttpStatusCode.BadRequest, voidRes.StatusCode);
            var err = await voidRes.Content.ReadAsStringAsync();
            Assert.Contains("already been sold", err);
            Assert.Contains("Record a return to the vendor instead", err);

            // Stock state and bill status remain intact
            AssertStockState(itemId, 2m, 10m);
            var reloaded = await (await Send(HttpMethod.Get, $"/api/Purchasing/Bill?id={approved.Id}")).Content.ReadFromJsonAsync<Dto.Purchasing.Bill>();
            Assert.Equal("Open", reloaded.Status);
        }

        [Fact]
        public async Task Bill_PayBill_RequiresDate_AndRejectsBeforeBillDate()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Pay Requirements Vendor" });
            var today = DateTime.UtcNow.Date;
            var bill = NewBill(vendor.Id, ("Line", 1, 50m, AccountId("50200")));
            bill.Date = today;
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);

            // Missing date
            var noDateRes = await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new Dto.Purchasing.PayBill
            {
                BillId = approved.Id,
                Amount = 50m,
                Date = null,
            });
            Assert.Equal(HttpStatusCode.BadRequest, noDateRes.StatusCode);
            Assert.Contains("Payment date is required", await noDateRes.Content.ReadAsStringAsync());

            // Date before bill date
            var earlyRes = await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new Dto.Purchasing.PayBill
            {
                BillId = approved.Id,
                Amount = 50m,
                Date = today.AddDays(-2),
            });
            Assert.Equal(HttpStatusCode.BadRequest, earlyRes.StatusCode);
            Assert.Contains("cannot be earlier than the bill date", await earlyRes.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Bill_SaveBill_RejectsNegativeCostOnStockLines()
        {
            var itemId = CreateStockItem("Negative Cost Stock Item");
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Negative Cost Vendor" });

            // Draft with negative unit cost on stock line is rejected
            var stockBill = NewBill(vendor.Id, ("Negative Stock", 2, -15m, AccountId("10800")));
            stockBill.Lines[0].ItemId = itemId;
            var res = await SaveBill(stockBill);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("cannot have a negative unit cost", await res.Content.ReadAsStringAsync());

            // Non-stock line with negative amount is allowed (e.g. vendor rebate / credit)
            var nonStockBill = NewBill(vendor.Id, ("Rebate", 1, -15m, AccountId("50200")));
            var nonStockOk = await SaveBillOk(nonStockBill);
            Assert.Equal(-15m, nonStockOk.Total);
        }

        [Fact]
        public async Task Bill_ApproveBill_RevalidatesArchivedAndPostable()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Archived Vendor Test" });
            var bill = NewBill(vendor.Id, ("Pending Line", 1, 100m, AccountId("50200")));
            var draft = await SaveBillOk(bill);
            Assert.Equal("Draft", draft.Status);

            // Archive the vendor
            WithDb(db =>
            {
                var v = db.Vendors.Single(x => x.Id == vendor.Id);
                v.ArchivedAt = DateTime.UtcNow;
                db.SaveChanges();
            });

            // Approving draft from archived vendor is rejected
            var approveRes = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={draft.Id}");
            Assert.Equal(HttpStatusCode.BadRequest, approveRes.StatusCode);
            Assert.Contains("archived", await approveRes.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Bill_SaveBill_WarnsPossibleDuplicate()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Duplicate Vendor" });
            var today = DateTime.UtcNow.Date;

            // Bill 1 without vendor invoice number
            var bill1 = NewBill(vendor.Id, ("Item A", 1, 75m, AccountId("50200")));
            bill1.VendorInvoiceNo = null;
            bill1.Date = today;
            var saved1 = await SaveBillOk(bill1);
            Assert.Equal(75m, saved1.Total);

            // Bill 2 with same vendor, same date, same total, no vendor invoice number
            var bill2 = NewBill(vendor.Id, ("Item B", 1, 75m, AccountId("50200")));
            bill2.VendorInvoiceNo = "";
            bill2.Date = today;

            var conflictRes = await SaveBill(bill2);
            Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
            var json = await Json(conflictRes);
            Assert.Equal("possible_duplicate", json.GetProperty("code").GetString());

            // Re-saving with ConfirmDuplicate = true succeeds
            bill2.ConfirmDuplicate = true;
            var confirmed = await SaveBillOk(bill2);
            Assert.NotEqual(saved1.Id, confirmed.Id);
            Assert.Equal(75m, confirmed.Total);
        }

        [Fact]
        public async Task Bill_VoidBill_RequiresReason()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Void Reason Vendor" });
            var bill = NewBill(vendor.Id, ("Line", 1, 50m, AccountId("50200")));
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);

            // Void without reason
            var voidRes = await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}");
            Assert.Equal(HttpStatusCode.BadRequest, voidRes.StatusCode);
            var err = await voidRes.Content.ReadAsStringAsync();
            Assert.Contains("reason is required to void a bill", err);

            // Void with empty/whitespace reason
            var voidEmptyRes = await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=   ");
            Assert.Equal(HttpStatusCode.BadRequest, voidEmptyRes.StatusCode);

            // Void with valid reason succeeds
            var voidOk = await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=Testing%20reason"));
            Assert.Equal("Void", voidOk.Status);
        }

        [Fact]
        public async Task Bill_VoidBill_WithTwoLinesOfSameStockItem_RejectsWhenCombinedStockSold()
        {
            var itemId = CreateStockItem("Combined Stock Void Item");
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Combined Stock Vendor" });

            // Receive 5 units on line 1 and 5 units on line 2 (total 10 units)
            var bill = NewBill(vendor.Id, ("Line 1", 5, 10m, AccountId("10800")), ("Line 2", 5, 10m, AccountId("10800")));
            bill.Lines[0].ItemId = itemId;
            bill.Lines[1].ItemId = itemId;
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            AssertStockState(itemId, 10m, 10m);

            // Sell 4 units on sales invoice (leaves 6 on hand)
            var invoice = await NewStockInvoice(itemId, 4m);
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issued.IsSuccessStatusCode);
            AssertStockState(itemId, 6m, 10m);

            // Individually, each line has 5 units which is <= 6 on hand.
            // But combined, 10 units were received and only 6 remain (4 were sold).
            // Voiding must fail in the pre-check before posting any reversals.
            var voidRes = await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=reversal");
            Assert.Equal(HttpStatusCode.BadRequest, voidRes.StatusCode);
            var err = await voidRes.Content.ReadAsStringAsync();
            Assert.Contains("4 of", err);
            Assert.Contains("have already been sold. Record a return to the vendor instead", err);

            // Stock state remains 6 units on hand
            AssertStockState(itemId, 6m, 10m);
        }

        [Fact]
        public async Task Bill_ApproveBill_WarnsPossibleDuplicate()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Approve Duplicate Vendor" });
            var today = DateTime.UtcNow.Date;

            // Bill 1: approved, no vendor invoice number
            var bill1 = NewBill(vendor.Id, ("Service A", 1, 150m, AccountId("50200")));
            bill1.VendorInvoiceNo = null;
            bill1.Date = today;
            bill1.Status = "Open";
            var saved1 = await SaveBillOk(bill1);
            Assert.Equal(150m, saved1.Total);

            // Bill 2: saved as Draft, no vendor invoice number, same vendor, date, total
            var bill2 = NewBill(vendor.Id, ("Service B", 1, 150m, AccountId("50200")));
            bill2.VendorInvoiceNo = "";
            bill2.Date = today;
            bill2.Status = "Draft";
            bill2.ConfirmDuplicate = true;
            var draft2 = await SaveBillOk(bill2);
            Assert.Equal("Draft", draft2.Status);

            // Approving draft2 without confirmDuplicate returns 409 Conflict
            var conflictRes = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={draft2.Id}");
            Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
            var json = await Json(conflictRes);
            Assert.Equal("possible_duplicate", json.GetProperty("code").GetString());

            // Approving draft2 with confirmDuplicate = true succeeds
            var approveOk = await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={draft2.Id}&confirmDuplicate=true"));
            Assert.Equal("Open", approveOk.Status);
            Assert.True(approveOk.Approved);
        }
    }
}
