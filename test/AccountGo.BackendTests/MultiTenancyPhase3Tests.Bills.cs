#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    // Purchases & Bills: vendors, bills, and what approving, paying and voiding them posts.
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Bill_DraftEditApprove_PostsEachLineToItsAccount_AgainstPayables()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Landlord & Co" });
            var equipment = AccountId("13302");
            var bill = NewBill(vendor.Id, ("Office rent, March", 1, 1000m, null), ("Laptop", 1, 1200m, equipment));
            bill.VendorInvoiceNo = "INV-77";

            var draft = await SaveBillOk(bill);
            Assert.Equal("Draft", draft.Status);
            Assert.False(draft.Approved);
            Assert.Null(draft.No);
            Assert.Equal(2200m, draft.Total);
            // Lines without an account get the vendor's purchase account; typed text is kept.
            Assert.Equal(AccountId("50200"), draft.Lines[0].AccountId);
            Assert.Equal("Office rent, March", draft.Lines[0].Description);
            Assert.Equal(equipment, draft.Lines[1].AccountId);

            // Editing a draft updates kept lines, adds new ones and drops the rest.
            draft.Lines[0].Amount = 1100m;
            draft.Lines.RemoveAt(1);
            draft.Lines.Add(new Dto.Purchasing.BillLine { Description = "Cleaning", Quantity = 2, Amount = 50m });
            var edited = await SaveBillOk(draft);
            Assert.Equal(new[] { "Office rent, March", "Cleaning" }, edited.Lines.Select(l => l.Description));
            Assert.Equal(edited.Lines[0].Id, draft.Lines[0].Id);
            Assert.Equal(1200m, edited.Total);
            Assert.Equal(0m, await DashboardKpi("totalPayables"));

            var approved = await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={edited.Id}"));
            Assert.Equal("Open", approved.Status);
            Assert.True(approved.Approved);
            Assert.False(string.IsNullOrEmpty(approved.No));
            Assert.Equal("Landlord & Co", approved.VendorName);

            var entry = Ledger(BillLedgerId(edited.Id));
            Assert.Equal(1200m, entry.Where(l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("50200")).Sum(l => l.Amount));
            Assert.Equal(1200m, entry.Where(l => l.DrCr == DrOrCrSide.Cr && l.AccountId == AccountId("20110")).Sum(l => l.Amount));
            Assert.Equal(entry.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount), entry.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount));
            Assert.Equal(1200m, await DashboardKpi("totalPayables"));

            // An approved bill is voided and entered again, not edited or deleted.
            approved.Lines[0].Amount = 1;
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveBill(approved)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Delete, $"/api/Purchasing/DeleteBill?id={edited.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={edited.Id}")).StatusCode);
        }

        [Fact]
        public async Task Bill_Payments_ClearPayables_AndVoidingReversesTheBill()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Paper Supply" });
            var bill = NewBill(vendor.Id, ("Paper", 10, 30m, null));
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            Assert.Equal(300m, approved.BalanceDue);

            var paid = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 100m, Date = DateTime.UtcNow, PaymentMethod = "Check", ReferenceNo = "CHK-1" }));
            Assert.Equal("Partially Paid", paid.Status);
            Assert.Equal(200m, paid.BalanceDue);
            var payment = Assert.Single(paid.Payments);
            Assert.Equal("Check", payment.PaymentMethod);
            Assert.NotNull(payment.AccountName);
            var paymentEntry = Ledger(PaymentLedgerId(payment.Id));
            Assert.Contains(paymentEntry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("20110") && l.Amount == 100m);
            Assert.Contains(paymentEntry, l => l.DrCr == DrOrCrSide.Cr && l.AccountId == payment.AccountId && l.Amount == 100m);

            // More than is owed, from a non-cash account, or on a bill with payments being voided: all refused.
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 200.01m, Date = DateTime.UtcNow })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 10m, Date = DateTime.UtcNow, AccountId = AccountId("50200") })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction")).StatusCode);

            var settled = await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = approved.Id, Amount = 200m, Date = DateTime.UtcNow }));
            Assert.Equal("Paid", settled.Status);
            Assert.Equal(0m, settled.BalanceDue);

            // Voiding an approved bill posts the reverse of its entry, so it no longer counts as owed or spent.
            var other = NewBill(vendor.Id, ("Toner", 1, 80m, null));
            other.Status = "Open";
            var toVoid = await SaveBillOk(other);
            Assert.Equal(80m, await DashboardKpi("totalPayables"));
            var voided = await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={toVoid.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction"));
            Assert.Equal("Void", voided.Status);
            Assert.Equal(0m, voided.BalanceDue);
            Assert.Equal(0m, await DashboardKpi("totalPayables"));
            var both = Ledger(BillLedgerId(toVoid.Id)).Concat(Ledger(BillVoidLedgerId(toVoid.Id))).ToList();
            foreach (var account in both.Select(l => l.AccountId).Distinct())
                Assert.Equal(0m, both.Where(l => l.AccountId == account).Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount));
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = toVoid.Id, Amount = 1m, Date = DateTime.UtcNow })).StatusCode);
        }

        [Fact]
        public async Task Bill_Tax_IsAddedOnTop_AndClaimedOnlyWithAPurchaseTaxAccount()
        {
            int claimable = 0, notClaimable = 0;
            WithDb(db =>
            {
                var input = new Core.Domain.TaxSystem.Tax { TaxCode = "IN10", TaxName = "Input 10%", Rate = 10, IsActive = true, PurchasingAccountId = AccountIdIn(db, "20300") };
                var local = new Core.Domain.TaxSystem.Tax { TaxCode = "LOC5", TaxName = "Local 5%", Rate = 5, IsActive = true };
                db.Taxes.AddRange(input, local);
                db.SaveChanges();
                claimable = input.Id;
                notClaimable = local.Id;
            });

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Taxed Supplier" });
            var bill = NewBill(vendor.Id, ("Software", 1, 1000m, null), ("Delivery", 1, 200m, null));
            bill.Lines[0].TaxId = claimable;
            bill.Lines[1].TaxId = notClaimable;
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            Assert.Equal(new[] { 100m, 10m }, approved.Lines.Select(l => l.TaxAmount));
            Assert.Equal(1200m, approved.Subtotal);
            Assert.Equal(1310m, approved.Total);

            var entry = Ledger(BillLedgerId(approved.Id));
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("20300") && l.Amount == 100m);
            // Tax that can't be claimed back is part of what the line cost.
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("50200") && l.Amount == 1210m);
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Cr && l.AccountId == AccountId("20110") && l.Amount == 1310m);
        }

        [Fact]
        public async Task Bill_Tax_DraftSaveAndRead_PreservesTaxIdAndCalculatesExclusiveTax()
        {
            int tax10Id = 0;
            WithDb(db =>
            {
                var tax = new Core.Domain.TaxSystem.Tax { TaxCode = "VAT10", TaxName = "VAT 10%", Rate = 10, IsActive = true, PurchasingAccountId = AccountIdIn(db, "20300") };
                db.Taxes.Add(tax);
                db.SaveChanges();
                tax10Id = tax.Id;
            });

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Draft Tax Vendor" });
            var bill = NewBill(vendor.Id, ("Line 1", 2, 100m, null), ("Line 2 Untaxed", 1, 50m, null));
            bill.Lines[0].TaxId = tax10Id;
            bill.Lines[0].Discount = 10m; // 2 * 100 = 200, 10% off = 180, tax 10% = 18
            bill.Lines[1].TaxId = null;   // 1 * 50 = 50, tax = 0
            bill.Status = "Draft";

            var draft = await SaveBillOk(bill);
            Assert.Equal("Draft", draft.Status);
            Assert.False(draft.Approved);
            Assert.Equal(230m, draft.Subtotal);
            Assert.Equal(18m, draft.TotalTax);
            Assert.Equal(248m, draft.Total);
            Assert.Equal(tax10Id, draft.Lines[0].TaxId);
            Assert.Equal(18m, draft.Lines[0].TaxAmount);
            Assert.Null(draft.Lines[1].TaxId);
            Assert.Equal(0m, draft.Lines[1].TaxAmount);

            // Re-read draft from GET /api/Purchasing/Bill?id=
            var reopenedResponse = await Send(HttpMethod.Get, $"/api/Purchasing/Bill?id={draft.Id}");
            var reopened = await BillOk(reopenedResponse);
            Assert.Equal(230m, reopened.Subtotal);
            Assert.Equal(18m, reopened.TotalTax);
            Assert.Equal(248m, reopened.Total);
            Assert.Equal(tax10Id, reopened.Lines[0].TaxId);
            Assert.Equal(18m, reopened.Lines[0].TaxAmount);
            Assert.Null(reopened.Lines[1].TaxId);
            Assert.Equal(0m, reopened.Lines[1].TaxAmount);

            // Editing draft: set Line 2 to use tax10Id as well, remove discount on Line 1
            reopened.Lines[0].Discount = null; // 2 * 100 = 200, tax 10% = 20
            reopened.Lines[1].TaxId = tax10Id; // 50, tax 10% = 5
            var updated = await SaveBillOk(reopened);
            Assert.Equal(250m, updated.Subtotal);
            Assert.Equal(25m, updated.TotalTax);
            Assert.Equal(275m, updated.Total);
            Assert.Equal(20m, updated.Lines[0].TaxAmount);
            Assert.Equal(5m, updated.Lines[1].TaxAmount);

            // Approve the updated draft
            var approved = await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={updated.Id}"));
            Assert.Equal("Open", approved.Status);
            Assert.True(approved.Approved);
            Assert.Equal(275m, approved.Total);

            var entry = Ledger(BillLedgerId(approved.Id));
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("20300") && l.Amount == 25m);
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("50200") && l.Amount == 250m);
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Cr && l.AccountId == AccountId("20110") && l.Amount == 275m);
        }

        [Fact]
        public async Task Bill_Tax_MixedRatesOnOneBill_PostsClaimableAndNonClaimableToAppropriateAccounts()
        {
            int claimableA = 0, claimableB = 0, nonClaimable = 0;
            WithDb(db =>
            {
                // 10160 = Input Tax (purchase), 20300 = Sales Tax Payable — both valid tax accounts; distinct so posting targets are checkable.
                var taxA = new Core.Domain.TaxSystem.Tax { TaxCode = "CL15", TaxName = "Claimable 15%", Rate = 15, IsActive = true, PurchasingAccountId = AccountIdIn(db, "10160") };
                var taxB = new Core.Domain.TaxSystem.Tax { TaxCode = "CL5", TaxName = "Claimable 5%", Rate = 5, IsActive = true, PurchasingAccountId = AccountIdIn(db, "20300") };
                var taxN = new Core.Domain.TaxSystem.Tax { TaxCode = "NC8", TaxName = "Non-Claimable 8%", Rate = 8, IsActive = true, PurchasingAccountId = null };
                db.Taxes.AddRange(taxA, taxB, taxN);
                db.SaveChanges();
                claimableA = taxA.Id;
                claimableB = taxB.Id;
                nonClaimable = taxN.Id;
            });

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Mixed Rates Supplier" });
            var equipmentAccount = AccountId("13302");
            var bill = NewBill(vendor.Id,
                ("Consulting", 1, 1000m, null),
                ("Supplies", 1, 200m, null),
                ("Laptop", 1, 500m, equipmentAccount),
                ("Exempt Service", 1, 300m, null));

            bill.Lines[0].TaxId = claimableA;    // Sub: 1000, Tax: 150 (Claimable to 10160 Input Tax)
            bill.Lines[1].TaxId = claimableB;    // Sub: 200, Tax: 10 (Claimable to 20300)
            bill.Lines[2].TaxId = nonClaimable;   // Sub: 500, Tax: 40 (Non-claimable, rolled into 13302)
            bill.Lines[3].TaxId = null;           // Sub: 300, Tax: 0 (Untaxed)
            bill.Status = "Open";

            var approved = await SaveBillOk(bill);
            Assert.Equal(2000m, approved.Subtotal);
            Assert.Equal(200m, approved.TotalTax);
            Assert.Equal(2200m, approved.Total);
            Assert.Equal(new decimal[] { 150m, 10m, 40m, 0m }, approved.Lines.Select(l => l.TaxAmount).ToArray());

            var entry = Ledger(BillLedgerId(approved.Id));
            // Claimable A → Input Tax
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("10160") && l.Amount == 150m);
            // Claimable B → distinct tax account
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("20300") && l.Amount == 10m);
            // Expense lines (1000 + 200 + 300 = 1500)
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == AccountId("50200") && l.Amount == 1500m);
            // Asset line: subtotal 500 + non-claimable tax 40 = 540
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Dr && l.AccountId == equipmentAccount && l.Amount == 540m);
            // Total AP credited: 2200
            Assert.Contains(entry, l => l.DrCr == DrOrCrSide.Cr && l.AccountId == AccountId("20110") && l.Amount == 2200m);
            Assert.Equal(entry.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount), entry.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount));
        }

        [Fact]
        public async Task Bill_Tax_Void_ReversesClaimableTaxAndPayables()
        {
            int claimable = 0;
            WithDb(db =>
            {
                var input = new Core.Domain.TaxSystem.Tax { TaxCode = "REV10", TaxName = "Reversible Tax 10%", Rate = 10, IsActive = true, PurchasingAccountId = AccountIdIn(db, "20300") };
                db.Taxes.Add(input);
                db.SaveChanges();
                claimable = input.Id;
            });

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Void Tax Vendor" });
            var bill = NewBill(vendor.Id, ("Service", 1, 1000m, null));
            bill.Lines[0].TaxId = claimable;
            bill.Status = "Open";

            var approved = await SaveBillOk(bill);
            Assert.Equal(1100m, approved.Total);
            Assert.Equal(100m, approved.TotalTax);

            var voided = await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={approved.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction"));
            Assert.Equal("Void", voided.Status);
            Assert.Equal(0m, voided.BalanceDue);

            var both = Ledger(BillLedgerId(approved.Id)).Concat(Ledger(BillVoidLedgerId(approved.Id))).ToList();
            foreach (var account in both.Select(l => l.AccountId).Distinct())
                Assert.Equal(0m, both.Where(l => l.AccountId == account).Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount));
        }

        [Fact]
        public async Task Bill_Tax_ForeignTax_IsRejectedOnSave()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Foreign Tax Vendor" });
            var bill = NewBill(vendor.Id, ("Item", 1, 100m, null));

            var org = await Send(HttpMethod.Post, "/api/organizations", new { name = "Foreign tax bill", slug = "foreign-tax-bill" }, scoped: false);
            Assert.Equal(HttpStatusCode.Created, org.StatusCode);
            var companyId = int.Parse((await Json(org)).GetProperty("id").GetString(), System.Globalization.CultureInfo.InvariantCulture);
            var foreignTaxId = 0;
            using (var scope = _factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(companyId, "foreign-tax-bill", CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                foreignTaxId = db.Taxes.First().Id;
            }

            bill.Lines[0].TaxId = foreignTaxId;
            var response = await SaveBill(bill);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("CROSS_TENANT_REFERENCE_DENIED", (await Json(response)).GetProperty("code").GetString());
            WithDb(db => Assert.DoesNotContain(db.PurchaseInvoiceLines, l => l.TaxId == foreignTaxId));
        }

        [Fact]
        public async Task SaveBill_ValidatesVendorNumberDatesAndAccounts()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Validator" });
            var first = NewBill(vendor.Id, ("Item", 1, 10m, null));
            first.VendorInvoiceNo = "A-1";
            await SaveBillOk(first);

            // The same vendor bill twice would be paid twice.
            var again = NewBill(vendor.Id, ("Item", 1, 10m, null));
            again.VendorInvoiceNo = "a-1";
            Assert.Equal(HttpStatusCode.Conflict, (await SaveBill(again)).StatusCode);
            var otherVendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Someone else" });
            again.VendorId = otherVendor.Id;
            Assert.True((await SaveBill(again)).IsSuccessStatusCode);

            var early = NewBill(vendor.Id, ("Item", 1, 10m, null));
            early.DueDate = early.Date.AddDays(-1);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveBill(early)).StatusCode);

            foreach (var code in new[] { "10111", "10120", "10110", "40100" })
            {
                var wrongAccount = NewBill(vendor.Id, ("Item", 1, 10m, AccountId(code)));
                Assert.Equal(HttpStatusCode.BadRequest, (await SaveBill(wrongAccount)).StatusCode);
            }
            var accounts = await (await Send(HttpMethod.Get, "/api/Purchasing/BillAccounts")).Content.ReadFromJsonAsync<List<Dto.Purchasing.BillAccount>>();
            Assert.Contains(accounts, a => a.Code == "50200" && a.Group == "Expense");
            Assert.Contains(accounts, a => a.Code == "13302" && a.Group == "Asset");
            Assert.DoesNotContain(accounts, a => a.Code is "10111" or "10120" or "10110");

            var blank = NewBill(vendor.Id, ("", 1, 10m, null));
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveBill(blank)).StatusCode);
            var zero = NewBill(vendor.Id, ("Nothing", 1, 0m, null));
            zero.Status = "Open";
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveBill(zero)).StatusCode);
            Assert.DoesNotContain((await Bills()), b => b.Total == 0m);
        }

        [Fact]
        public async Task Bills_DraftingApprovingAndPaying_FollowTheRoles()
        {
            // A purchasing agent drafts bills; approving and paying them is the accountant's.
            await JoinOther();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { RoleId("Purchasing Agent") } })).StatusCode);

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Role check" });
            var bill = NewBill(vendor.Id, ("Item", 1, 10m, null));
            bill.Status = "Open";
            var approvedOnSave = await SaveBill(bill, _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, approvedOnSave.StatusCode);
            Assert.Contains("purchases.bills.post", await approvedOnSave.Content.ReadAsStringAsync());

            bill.Status = "Draft";
            var draft = await BillOk(await SaveBill(bill, _otherToken));
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={draft.Id}", token: _otherToken)).StatusCode);
            await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={draft.Id}"));
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = draft.Id, Amount = 5m, Date = DateTime.UtcNow }, _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={draft.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction", token: _otherToken)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { RoleId("Accountant") } })).StatusCode);
            Assert.True((await Send(HttpMethod.Post, "/api/Purchasing/PayBill",
                new Dto.Purchasing.PayBill { BillId = draft.Id, Amount = 5m, Date = DateTime.UtcNow }, _otherToken)).IsSuccessStatusCode);

            // A viewer sees bills and vendors but changes nothing.
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { ViewerRoleId() } })).StatusCode);
            Assert.True((await Send(HttpMethod.Get, "/api/Purchasing/Bills", token: _otherToken)).IsSuccessStatusCode);
            Assert.True((await Send(HttpMethod.Get, "/api/Purchasing/Vendors", token: _otherToken)).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SaveBill(bill, _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Purchasing/SaveVendor",
                new Dto.Purchasing.Supplier { Name = "Nope" }, _otherToken)).StatusCode);
        }

        [SqliteFact]
        public async Task Vendors_Balances_MatchTheBills_AndOnlyUnusedVendorsAreDeleted()
        {
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Balances Ltd", Email = "ap@balances.example", Street = "1 Main St", City = "Makati" });
            Assert.True(vendor.CanDelete);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Purchasing/SaveVendor",
                new Dto.Purchasing.Supplier { Name = "Bad email", Email = "nope" })).StatusCode);

            async Task<Dto.Purchasing.Bill> Bill(string status, decimal amount, DateTime date, DateTime? due = null)
            {
                var dto = NewBill(vendor.Id, ("Line", 1, amount, null));
                dto.Status = status;
                dto.Date = date;
                dto.DueDate = due;
                return await SaveBillOk(dto);
            }

            var today = DateTime.UtcNow.Date;
            var open = await Bill("Open", 100m, today);
            var overdue = await Bill("Open", 40m, today.AddDays(-40), today.AddDays(-10));
            var draft = await Bill("Draft", 999m, today.AddDays(1));
            var voided = await Bill("Open", 500m, today.AddDays(-5));
            Assert.Equal("Overdue", overdue.Status);
            await BillOk(await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={voided.Id}&date={IsoDate(today)}&reason=correction"));
            await BillOk(await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new Dto.Purchasing.PayBill { BillId = open.Id, Amount = 30m, Date = today }));

            var bills = await Bills(vendor.Id);
            Assert.Equal(4, bills.Count);
            foreach (var v in new[] { (await Vendors()).Single(v => v.Id == vendor.Id), await GetVendor(vendor.Id) })
            {
                Assert.Equal(70m + 40m, v.Outstanding);
                Assert.Equal(40m, v.Overdue);
                Assert.Equal(30m, v.PaidToDate);
                Assert.Equal(today, v.LastBillDate?.Date);
                Assert.Equal("1 Main St", v.Street);
            }
            Assert.Equal(110m, await DashboardKpi("totalPayables"));
            Assert.False((await GetVendor(vendor.Id)).CanDelete);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Delete, $"/api/Purchasing/DeleteVendor?id={vendor.Id}")).StatusCode);

            // Archived vendors keep their bills, and take no new ones until restored.
            var archived = await Json(await Send(HttpMethod.Post, $"/api/Purchasing/ArchiveVendor?id={vendor.Id}"));
            Assert.True(archived.GetProperty("archived").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveBill(NewBill(vendor.Id, ("Line", 1, 1m, null)))).StatusCode);
            Assert.Equal(4, (await Bills(vendor.Id)).Count);

            // A draft deleted removes its lines before the bill, the order D1 replays and enforces foreign keys in.
            var unused = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Mistake" });
            var mistake = await SaveBillOk(NewBill(unused.Id, ("Oops", 1, 1m, null)));
            Assert.False((await GetVendor(unused.Id)).CanDelete);
            var deleted = await Send(HttpMethod.Delete, $"/api/Purchasing/DeleteBill?id={mistake.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            var statements = AllSyncStatements(deleted);
            var line = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"PurchaseInvoiceLine\""));
            var header = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"PurchaseInvoiceHeader\""));
            Assert.True(line >= 0 && header > line, string.Join("\n", statements));
            Assert.True((await GetVendor(unused.Id)).CanDelete);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/Purchasing/DeleteVendor?id={unused.Id}")).StatusCode);
            Assert.DoesNotContain(await Vendors(), v => v.Id == unused.Id);
        }

        [Fact]
        public async Task VoidingAnIssuedInvoice_ReversesItsPosting()
        {
            var invoice = await NewInvoice();
            // A typed line is booked to the general service item, which posts.
            invoice.SalesInvoiceLines[0].ItemId = null;
            invoice.SalesInvoiceLines[0].ItemDescription = "Advisory hours";
            invoice.Status = "Open";
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            var id = (await Json(issued)).GetProperty("id").GetInt32();
            Assert.Equal(100m, await OutstandingReceivables());

            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}&date={IsoDate(DateTime.UtcNow)}")).IsSuccessStatusCode);
            // Nothing is due on a voided invoice, in the invoice and in the list.
            var voided = await GetInvoice(id);
            Assert.Equal("Void", voided.Status);
            Assert.Equal(0m, voided.BalanceDue);
            var listed = await (await Send(HttpMethod.Get, "/api/Sales/SalesInvoices")).Content.ReadFromJsonAsync<List<Dto.Sales.SalesInvoice>>();
            Assert.Equal(0m, listed.Single(i => i.Id == id).BalanceDue);
            int posted = 0, reversal = 0;
            WithDb(db =>
            {
                var header = db.SalesInvoiceHeaders.Single(i => i.Id == id);
                posted = header.GeneralLedgerHeaderId.Value;
                reversal = header.VoidGeneralLedgerHeaderId.Value;
            });
            var both = Ledger(posted).Concat(Ledger(reversal)).ToList();
            Assert.NotEmpty(Ledger(reversal));
            foreach (var account in both.Select(l => l.AccountId).Distinct())
                Assert.Equal(0m, both.Where(l => l.AccountId == account).Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount));

            // Voiding again doesn't reverse twice.
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={id}&date={IsoDate(DateTime.UtcNow)}")).IsSuccessStatusCode);
            WithDb(db => Assert.Equal(reversal, db.SalesInvoiceHeaders.Single(i => i.Id == id).VoidGeneralLedgerHeaderId));
        }

        [SqliteFact]
        public async Task BillsMigration_BackfillsLegacyBills_AndReversesVoidedInvoices()
        {
            // An invoice voided before voids reversed their posting.
            var invoice = await NewInvoice();
            invoice.SalesInvoiceLines[0].ItemId = null;
            invoice.SalesInvoiceLines[0].ItemDescription = "Advisory hours";
            invoice.Status = "Open";
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            var invoiceId = (await Json(issued)).GetProperty("id").GetInt32();
            WithDb(db =>
            {
                db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId).Status = SalesInvoiceStatus.Void;
                db.SaveChanges();
            });
            // A bill saved before bills had statuses or line text.
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Legacy vendor" });
            var legacy = await SaveBillOk(NewBill(vendor.Id, ("Legacy line", 1, 10m, null)));

            using var source = new SqliteConnection("Data Source=" + _path);
            using var replica = new SqliteConnection("Data Source=:memory:");
            source.Open();
            replica.Open();
            source.BackupDatabase(replica);
            using var command = replica.CreateCommand();
            // D1's data as it is before the migration. The test schema is built with foreign keys, so the new columns
            // stay and the migration runs without its ALTER TABLE statements.
            command.CommandText = $@"
                UPDATE ""PurchaseInvoiceHeader"" SET ""Status"" = NULL WHERE ""Id"" = {legacy.Id};
                UPDATE ""PurchaseInvoiceLine"" SET ""Description"" = NULL, ""AccountId"" = NULL WHERE ""PurchaseInvoiceHeaderId"" = {legacy.Id};
                DELETE FROM ""SecurityRolePermission"" WHERE ""SecurityPermissionId"" IN (SELECT ""Id"" FROM ""SecurityPermission"" WHERE ""Name"" = 'purchases.payments.create');
                DELETE FROM ""SecurityPermission"" WHERE ""Name"" = 'purchases.payments.create';";
            command.ExecuteNonQuery();
            command.CommandText = string.Join("\n", File.ReadAllLines(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0012_bills.sql"))
                .Where(line => !line.StartsWith("ALTER TABLE", StringComparison.Ordinal)));
            command.ExecuteNonQuery();

            command.CommandText = $@"SELECT ""Status"" FROM ""PurchaseInvoiceHeader"" WHERE ""Id"" = {legacy.Id}";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = $@"SELECT ""Description"" FROM ""PurchaseInvoiceLine"" WHERE ""PurchaseInvoiceHeaderId"" = {legacy.Id}";
            Assert.Equal("Service / Product", command.ExecuteScalar());

            command.CommandText = $@"
                SELECT SUM(CASE l.""DrCr"" WHEN 1 THEN l.""Amount"" ELSE -l.""Amount"" END), COUNT(*)
                FROM ""SalesInvoiceHeader"" i JOIN ""GeneralLedgerLine"" l
                  ON l.""GeneralLedgerHeaderId"" IN (i.""GeneralLedgerHeaderId"", i.""VoidGeneralLedgerHeaderId"")
                WHERE i.""Id"" = {invoiceId} AND l.""AccountId"" = {AccountId("10120")}";
            using (var reader = command.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal(0m, reader.GetDecimal(0));
                Assert.Equal(2L, reader.GetInt64(1));
            }

            command.CommandText = @"SELECT COUNT(*) FROM ""SecurityRolePermission"" rp
                JOIN ""SecurityPermission"" p ON p.""Id"" = rp.""SecurityPermissionId""
                JOIN ""SecurityRole"" r ON r.""Id"" = rp.""SecurityRoleId""
                WHERE p.""Name"" = 'purchases.payments.create' AND r.""Name"" = 'Accountant'";
            Assert.Equal(1L, command.ExecuteScalar());
        }

        private static Dto.Purchasing.Bill NewBill(int vendorId, params (string Description, decimal Quantity, decimal Amount, int? AccountId)[] lines) => new()
        {
            VendorId = vendorId,
            Date = DateTime.UtcNow.Date,
            Status = "Draft",
            Lines = lines.Select(l => new Dto.Purchasing.BillLine { Description = l.Description, Quantity = l.Quantity, Amount = l.Amount, AccountId = l.AccountId }).ToList(),
        };

        private Task<HttpResponseMessage> SaveBill(Dto.Purchasing.Bill bill, string token = null) =>
            Send(HttpMethod.Post, "/api/Purchasing/SaveBill", bill, token);

        private async Task<Dto.Purchasing.Bill> SaveBillOk(Dto.Purchasing.Bill bill) => await BillOk(await SaveBill(bill));

        private static async Task<Dto.Purchasing.Bill> BillOk(HttpResponseMessage response)
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Dto.Purchasing.Bill>();
        }

        private async Task<List<Dto.Purchasing.Bill>> Bills(int? vendorId = null) =>
            await (await Send(HttpMethod.Get, "/api/Purchasing/Bills" + (vendorId.HasValue ? $"?vendorId={vendorId}" : ""))).Content
                .ReadFromJsonAsync<List<Dto.Purchasing.Bill>>();

        private async Task<Dto.Purchasing.Supplier> SaveVendor(Dto.Purchasing.Supplier vendor)
        {
            var response = await Send(HttpMethod.Post, "/api/Purchasing/SaveVendor", vendor);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Dto.Purchasing.Supplier>();
        }

        private async Task<Dto.Purchasing.Supplier> GetVendor(int id)
        {
            var response = await Send(HttpMethod.Get, $"/api/Purchasing/Vendor?id={id}");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Dto.Purchasing.Supplier>();
        }

        private async Task<List<Dto.Purchasing.Supplier>> Vendors() =>
            await (await Send(HttpMethod.Get, "/api/Purchasing/Vendors")).Content.ReadFromJsonAsync<List<Dto.Purchasing.Supplier>>();

        private void WithDb(Action<ApiDbContext> action)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                action(db);
        }

        private int AccountId(string code)
        {
            var id = 0;
            WithDb(db => id = AccountIdIn(db, code));
            return id;
        }

        private static int AccountIdIn(ApiDbContext db, string code) => db.Accounts.Single(a => a.AccountCode == code).Id;

        private int BillLedgerId(int billId)
        {
            var id = 0;
            WithDb(db => id = db.PurchaseInvoiceHeaders.Single(b => b.Id == billId).GeneralLedgerHeaderId.Value);
            return id;
        }

        private int BillVoidLedgerId(int billId)
        {
            var id = 0;
            WithDb(db => id = db.PurchaseInvoiceHeaders.Single(b => b.Id == billId).VoidGeneralLedgerHeaderId.Value);
            return id;
        }

        private int PaymentLedgerId(int paymentId)
        {
            var id = 0;
            WithDb(db => id = db.VendorPayments.Single(p => p.Id == paymentId).GeneralLedgerHeaderId.Value);
            return id;
        }

        private List<Core.Domain.Financials.GeneralLedgerLine> Ledger(int generalLedgerHeaderId)
        {
            List<Core.Domain.Financials.GeneralLedgerLine> lines = null;
            WithDb(db => lines = db.GeneralLedgerLines.AsNoTracking().Where(l => l.GeneralLedgerHeaderId == generalLedgerHeaderId).ToList());
            return lines;
        }
    }
}
