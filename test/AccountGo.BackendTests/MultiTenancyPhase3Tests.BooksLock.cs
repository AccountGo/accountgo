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
using Core.Domain.Sales;
using Core.Domain.Security;
using Dto.Banking;
using Xunit;

namespace AccountGo.BackendTests
{
    /// <summary>Phase 2a of #455: books lock API and refused postings (real SQLite).</summary>
    public partial class MultiTenancyPhase3Tests
    {
        private static string LockMessage(DateTime lockedThrough) =>
            BooksLockedException.FormatMessage(lockedThrough);

        [Fact]
        public async Task BooksLock_PutLaterWithoutReason_WritesHistory_And_BlocksLockedPosting()
        {
            var lockDate = DateTime.UtcNow.Date.AddDays(-30);
            var put = await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(lockDate) });
            Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
            var body = await put.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(IsoDate(lockDate), body.GetProperty("lockedThrough").GetString());
            Assert.NotEmpty(body.GetProperty("history").EnumerateArray());

            WithDb(db =>
            {
                Assert.Equal(lockDate, db.CompanySettings.Single().BooksLockedThrough!.Value.Date);
                Assert.Single(db.BooksLockChanges);
                Assert.Equal(lockDate, db.BooksLockChanges.Single().NewLockedThrough!.Value.Date);
            });

            var invoice = await NewInvoice();
            invoice.InvoiceDate = lockDate;
            invoice.Status = "Open";
            var refused = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var refusedBody = await refused.Content.ReadAsStringAsync();
            Assert.Contains(LockMessage(lockDate), refusedBody, StringComparison.Ordinal);

            invoice.InvoiceDate = lockDate.AddDays(1);
            var ok = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task BooksLock_ReopenRequiresReason_AndPermission()
        {
            var later = DateTime.UtcNow.Date.AddDays(-10);
            var earlier = DateTime.UtcNow.Date.AddDays(-40);
            Assert.True((await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(later) })).IsSuccessStatusCode);

            var missingReason = await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(earlier) });
            Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

            var clearMissing = await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = (string)null });
            Assert.Equal(HttpStatusCode.BadRequest, clearMissing.StatusCode);

            var reopen = await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(earlier), reason = "Correct VAT filing month" });
            Assert.True(reopen.IsSuccessStatusCode, await reopen.Content.ReadAsStringAsync());
            WithDb(db => Assert.Equal(2, db.BooksLockChanges.Count()));

            var clear = await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = (string)null, reason = "Unlock for late entries" });
            Assert.True(clear.IsSuccessStatusCode, await clear.Content.ReadAsStringAsync());

            var future = await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(DateTime.UtcNow.Date.AddDays(2)) });
            Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

            await JoinOther();
            var roleId = 0;
            WithDb(db =>
            {
                var role = new SecurityRole { Name = "Books lock viewer", DisplayName = "Books lock viewer" };
                role.Permissions.Add(new SecurityRolePermission
                {
                    SecurityPermissionId = db.SecurityPermissions.Single(p => p.Name == SystemPermissions.FinancialsAccountsView).Id
                });
                db.SecurityRoles.Add(role);
                db.SaveChanges();
                roleId = role.Id;
            });
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { roleId } })).StatusCode);
            var denied = await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(later) }, token: _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        [Fact]
        public async Task BooksLock_VoidOfOldInvoice_IsRefused()
        {
            var invoice = await NewInvoice();
            invoice.InvoiceDate = DateTime.UtcNow.Date.AddDays(-60);
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var lockDate = DateTime.UtcNow.Date.AddDays(-30);
            Assert.True((await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(lockDate) })).IsSuccessStatusCode);

            var voidResponse = await Send(HttpMethod.Post,
                $"/api/Sales/VoidSalesInvoice?id={id}&date={IsoDate(DateTime.UtcNow.Date)}");
            Assert.Equal(HttpStatusCode.BadRequest, voidResponse.StatusCode);
            Assert.Contains(LockMessage(lockDate), await voidResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task BooksLock_InvoicingOnlyWorkspace_IsNotBlockedEvenWithLockInDb()
        {
            WithDb(db =>
            {
                var company = db.Companies.Single(c => c.Id == _companyId);
                company.Plan = "invoicing";
                company.DisabledModulesJson = "[\"financials\"]";
                db.CompanySettings.Single().BooksLockedThrough = DateTime.UtcNow.Date.AddDays(-30);
                db.SaveChanges();
            });

            // Force entitlement cache refresh by resolving tenant again through a request.
            var invoice = await NewInvoice();
            invoice.InvoiceDate = DateTime.UtcNow.Date.AddDays(-60);
            invoice.Status = "Open";
            var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task BooksLock_OpeningBalance_OnOrBeforeLock_IsRefused()
        {
            var lockDate = DateTime.UtcNow.Date.AddDays(-30);
            Assert.True((await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(lockDate) })).IsSuccessStatusCode);

            var refused = await Send(HttpMethod.Post, "/api/Banking/Accounts", new Dto.Banking.CreateBankAccountRequest
            {
                Name = "Locked OB",
                Type = 1,
                OpeningBalance = 100m,
                OpeningBalanceDate = lockDate
            });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains(LockMessage(lockDate), await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var ok = await Send(HttpMethod.Post, "/api/Banking/Accounts", new Dto.Banking.CreateBankAccountRequest
            {
                Name = "Open OB",
                Type = 1,
                OpeningBalance = 100m,
                OpeningBalanceDate = lockDate.AddDays(1)
            });
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task BooksLock_UpdateReceipt_RefusesExistingOrNewDateOnOrBeforeLock()
        {
            var lockDate = DateTime.UtcNow.Date.AddDays(-30);
            var bank = await CreateBankOk(new CreateBankAccountRequest { Name = "Lock Receipt Bank", Type = 1 });
            var invoice = await NewInvoice();

            int customerId = invoice.CustomerId, creditAccountId = 0, lockedReceiptId = 0, openReceiptId = 0;
            WithDb(db =>
            {
                var customer = db.Customers.First(c => c.Id == customerId);
                creditAccountId = customer.CustomerAdvancesAccountId
                    ?? throw new InvalidOperationException("Customer advances account missing");
                var cashAccount = db.Accounts.First(a => a.AccountCode == "10110");

                var lockedReceipt = new SalesReceiptHeader
                {
                    CompanyId = _companyId,
                    CustomerId = customerId,
                    Date = lockDate,
                    Amount = 40m,
                    AccountToDebitId = cashAccount.Id,
                    No = Guid.NewGuid().ToString("N")
                };
                lockedReceipt.SalesReceiptLines.Add(new SalesReceiptLine
                {
                    Amount = 40m,
                    AmountPaid = 40m,
                    AccountToCreditId = creditAccountId
                });
                db.SalesReceiptHeaders.Add(lockedReceipt);

                var openReceipt = new SalesReceiptHeader
                {
                    CompanyId = _companyId,
                    CustomerId = customerId,
                    Date = lockDate.AddDays(1),
                    Amount = 25m,
                    AccountToDebitId = cashAccount.Id,
                    No = Guid.NewGuid().ToString("N")
                };
                openReceipt.SalesReceiptLines.Add(new SalesReceiptLine
                {
                    Amount = 25m,
                    AmountPaid = 25m,
                    AccountToCreditId = creditAccountId
                });
                db.SalesReceiptHeaders.Add(openReceipt);
                db.SaveChanges();
                lockedReceiptId = lockedReceipt.Id;
                openReceiptId = openReceipt.Id;
            });

            Assert.True((await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(lockDate) })).IsSuccessStatusCode);

            var refusedExisting = await Send(HttpMethod.Post, "/api/Sales/UpdateReceipt", new
            {
                Id = lockedReceiptId,
                CustomerId = customerId,
                ReceiptDate = lockDate.AddDays(5),
                Amount = 40m,
                AccountToDebitId = bank.Id,
                AccountToCreditId = creditAccountId
            });
            Assert.Equal(HttpStatusCode.BadRequest, refusedExisting.StatusCode);
            Assert.Contains(LockMessage(lockDate), await refusedExisting.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var refusedNew = await Send(HttpMethod.Post, "/api/Sales/UpdateReceipt", new
            {
                Id = openReceiptId,
                CustomerId = customerId,
                ReceiptDate = lockDate,
                Amount = 25m,
                AccountToDebitId = bank.Id,
                AccountToCreditId = creditAccountId
            });
            Assert.Equal(HttpStatusCode.BadRequest, refusedNew.StatusCode);
            Assert.Contains(LockMessage(lockDate), await refusedNew.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var ok = await Send(HttpMethod.Post, "/api/Sales/UpdateReceipt", new
            {
                Id = openReceiptId,
                CustomerId = customerId,
                ReceiptDate = lockDate.AddDays(2),
                Amount = 30m,
                AccountToDebitId = bank.Id,
                AccountToCreditId = creditAccountId
            });
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task BooksLock_SaveAllocation_RefusesAllocationDateOnOrBeforeLock()
        {
            var draftId = await SaveDraft();
            await Issue(draftId);
            var invoice = await GetInvoice(draftId);

            int receiptId = 0, creditAccountId = 0;
            WithDb(db =>
            {
                var customer = db.Customers.First(c => c.Id == invoice.CustomerId);
                creditAccountId = customer.CustomerAdvancesAccountId
                    ?? throw new InvalidOperationException("Customer advances account missing");
                var cashAccount = db.Accounts.First(a => a.AccountCode == "10110");
                var receipt = new SalesReceiptHeader
                {
                    CompanyId = _companyId,
                    CustomerId = customer.Id,
                    Date = DateTime.UtcNow,
                    Amount = invoice.TotalAmountAfterTax.GetValueOrDefault(),
                    AccountToDebitId = cashAccount.Id,
                    No = Guid.NewGuid().ToString("N")
                };
                receipt.SalesReceiptLines.Add(new SalesReceiptLine
                {
                    Amount = invoice.TotalAmountAfterTax.GetValueOrDefault(),
                    AmountPaid = invoice.TotalAmountAfterTax.GetValueOrDefault(),
                    AccountToCreditId = creditAccountId
                });
                db.SalesReceiptHeaders.Add(receipt);
                db.SaveChanges();
                receiptId = receipt.Id;
            });

            var lockDate = DateTime.UtcNow.Date.AddDays(-30);
            Assert.True((await Send(HttpMethod.Put, "/api/organizations/current/books-lock",
                new { lockedThrough = IsoDate(lockDate) })).IsSuccessStatusCode);

            var refused = await Send(HttpMethod.Post, "/api/Sales/SaveAllocation",
                new Api.Controllers.SalesController.SaveAllocationDto
                {
                    CustomerId = invoice.CustomerId,
                    ReceiptId = receiptId,
                    Date = lockDate,
                    AllocationLines = new List<Api.Controllers.SalesController.SaveAllocationLineDto>
                    {
                        new() { InvoiceId = invoice.Id, AmountToAllocate = 1m }
                    }
                });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains(LockMessage(lockDate), await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var ok = await Send(HttpMethod.Post, "/api/Sales/SaveAllocation",
                new Api.Controllers.SalesController.SaveAllocationDto
                {
                    CustomerId = invoice.CustomerId,
                    ReceiptId = receiptId,
                    Date = lockDate.AddDays(1),
                    AllocationLines = new List<Api.Controllers.SalesController.SaveAllocationLineDto>
                    {
                        new() { InvoiceId = invoice.Id, AmountToAllocate = 1m }
                    }
                });
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        }
    }
}
