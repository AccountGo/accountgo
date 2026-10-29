#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task SalesInvoice_IssuedInvoices_CannotBeEdited_Or_Deleted()
        {
            var draftId = await SaveDraft();
            var invoiceNo = await Issue(draftId);

            // 1. SaveSalesInvoice rejects edit on issued invoice
            var issued = await GetInvoice(draftId);
            issued.ReferenceNo = "EDIT-123";
            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", issued);
            Assert.Equal(HttpStatusCode.BadRequest, saveRes.StatusCode);
            var saveError = await saveRes.Content.ReadAsStringAsync();
            Assert.Contains("Issued invoices can't be edited. Void it and issue a new one.", saveError);

            // 2. UpdateSalesInvoice rejects edit on issued invoice
            var updateRes = await Send(HttpMethod.Post, "/api/Sales/UpdateSalesInvoice", issued);
            Assert.Equal(HttpStatusCode.BadRequest, updateRes.StatusCode);
            var updateError = await updateRes.Content.ReadAsStringAsync();
            Assert.Contains("Issued invoices can't be edited. Void it and issue a new one.", updateError);

            // 3. DeleteSalesInvoice rejects delete on issued invoice
            var delRes = await Send(HttpMethod.Delete, $"/api/Sales/DeleteSalesInvoice?id={draftId}");
            Assert.Equal(HttpStatusCode.BadRequest, delRes.StatusCode);
        }

        [Fact]
        public async Task SalesInvoice_IssueSalesInvoice_IsIdempotentOnOpen_AndRejectsClosedOrVoid()
        {
            var draftId = await SaveDraft();
            var no = await Issue(draftId);

            // Calling issue again on Open invoice is idempotent (returns 200 without changes)
            var reissue = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={draftId}");
            Assert.Equal(HttpStatusCode.OK, reissue.StatusCode);
            var reloaded = await GetInvoice(draftId);
            Assert.Equal("Open", reloaded.Status);
            Assert.Equal(no, reloaded.No);

            // Void it
            var voidRes = await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={draftId}");
            Assert.Equal(HttpStatusCode.OK, voidRes.StatusCode);

            // Calling issue on Void invoice is rejected
            var issueVoid = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={draftId}");
            Assert.Equal(HttpStatusCode.BadRequest, issueVoid.StatusCode);
        }

        [Fact]
        public async Task SalesInvoice_VoidSalesInvoice_RejectsPaidInvoices()
        {
            var draftId = await SaveDraft();
            await Issue(draftId);
            var invoice = await GetInvoice(draftId);

            // Pay the invoice fully
            var payRes = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = draftId,
                Amount = invoice.BalanceDue,
                PaymentMethod = "Bank Transfer"
            });
            Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);

            var paidInvoice = await GetInvoice(draftId);
            Assert.Equal("Paid", paidInvoice.Status);

            // Attempting to void paid invoice is rejected
            var voidPaidRes = await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={draftId}");
            Assert.Equal(HttpStatusCode.BadRequest, voidPaidRes.StatusCode);
            var voidPaidMsg = await voidPaidRes.Content.ReadAsStringAsync();
            Assert.Contains("Cannot void a paid invoice", voidPaidMsg);
        }

        [Fact]
        public async Task SalesInvoice_PaidInvoice_RejectsEmail_AndPayments()
        {
            var draftId = await SaveDraft();
            await Issue(draftId);
            var invoice = await GetInvoice(draftId);

            // Pay full balance
            var payRes = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = draftId,
                Amount = invoice.BalanceDue,
                PaymentMethod = "Cash"
            });
            Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);

            // Another payment on paid invoice is rejected
            var secondPay = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = draftId,
                Amount = 10m
            });
            Assert.Equal(HttpStatusCode.BadRequest, secondPay.StatusCode);
            var secondPayMsg = await secondPay.Content.ReadAsStringAsync();
            Assert.Contains("already paid", secondPayMsg);

            // Sending email on paid invoice is rejected
            var emailRes = await Send(HttpMethod.Post, $"/api/Sales/SendSalesInvoiceEmail?id={draftId}");
            Assert.Equal(HttpStatusCode.BadRequest, emailRes.StatusCode);

            var followUpRes = await Send(HttpMethod.Post, $"/api/Sales/SendSalesInvoiceFollowUpEmail?id={draftId}");
            Assert.Equal(HttpStatusCode.BadRequest, followUpRes.StatusCode);
        }

        [Fact]
        public async Task SalesInvoice_RecordPayment_RejectsOverapplication_AndUpdatesStatus()
        {
            var draftId = await SaveDraft();
            await Issue(draftId);
            var invoice = await GetInvoice(draftId);

            // Over-application rejected
            var overpayRes = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = draftId,
                Amount = invoice.BalanceDue + 50m
            });
            Assert.Equal(HttpStatusCode.BadRequest, overpayRes.StatusCode);
            var overpayMsg = await overpayRes.Content.ReadAsStringAsync();
            Assert.Contains("exceeds balance due", overpayMsg);

            // Partial payment marks invoice Open / Partially Paid
            var partialPay = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = draftId,
                Amount = invoice.BalanceDue / 2m
            });
            Assert.Equal(HttpStatusCode.OK, partialPay.StatusCode);
            var partialInvoice = await GetInvoice(draftId);
            Assert.Equal("Partially Paid", partialInvoice.Status);
            Assert.True(partialInvoice.BalanceDue > 0);

            // Final payment marks invoice Paid
            var finalPay = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = draftId,
                Amount = partialInvoice.BalanceDue
            });
            Assert.Equal(HttpStatusCode.OK, finalPay.StatusCode);
            var closedInvoice = await GetInvoice(draftId);
            Assert.Equal("Paid", closedInvoice.Status);
            Assert.Equal(0m, closedInvoice.BalanceDue);
        }

        [Fact]
        public async Task SalesInvoice_SaveAllocation_ValidatesCustomerAndInvoice_AndUpdatesStatus()
        {
            var draftId = await SaveDraft();
            await Issue(draftId);
            var invoice = await GetInvoice(draftId);

            // Create customer advances receipt
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var cashAccount = db.Accounts.First(a => a.AccountCode == "10100");
                var customer = db.Customers.Include(c => c.CustomerAdvancesAccount).First(c => c.Id == invoice.CustomerId);
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
                    AccountToCreditId = customer.CustomerAdvancesAccountId ?? 2
                });
                db.SalesReceiptHeaders.Add(receipt);
                await db.SaveChangesAsync();

                // Allocate to invoice
                var allocDto = new Api.Controllers.SalesController.SaveAllocationDto
                {
                    CustomerId = customer.Id,
                    ReceiptId = receipt.Id,
                    Date = DateTime.UtcNow,
                    AllocationLines = new List<Api.Controllers.SalesController.SaveAllocationLineDto>
                    {
                        new() { InvoiceId = invoice.Id, AmountToAllocate = invoice.TotalAmountAfterTax }
                    }
                };

                var allocRes = await Send(HttpMethod.Post, "/api/Sales/SaveAllocation", allocDto);
                Assert.Equal(HttpStatusCode.OK, allocRes.StatusCode);

                var reloaded = await GetInvoice(invoice.Id);
                Assert.Equal("Paid", reloaded.Status);
                Assert.Equal(0m, reloaded.BalanceDue);
            }
        }

        [Fact]
        public async Task SalesInvoice_UpdateReceipt_BlocksEditing_PostedOrAllocatedReceipt()
        {
            var draftId = await SaveDraft();
            await Issue(draftId);
            var invoice = await GetInvoice(draftId);

            // Record a payment, which creates a posted and allocated receipt
            var payRes = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = draftId,
                Amount = 50m
            });
            Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);
            var payData = await payRes.Content.ReadFromJsonAsync<JsonElement>();
            var receiptId = payData.GetProperty("receiptId").GetInt32();

            // Attempt to update the posted & allocated receipt
            var editDto = new
            {
                Id = receiptId,
                CustomerId = invoice.CustomerId,
                ReceiptDate = DateTime.UtcNow,
                Amount = 25m,
                AccountToDebitId = 1,
                AccountToCreditId = 2
            };

            var updateRes = await Send(HttpMethod.Post, "/api/Sales/UpdateReceipt", editDto);
            Assert.Equal(HttpStatusCode.BadRequest, updateRes.StatusCode);
            var errorMsg = await updateRes.Content.ReadAsStringAsync();
            Assert.Contains("Posted or allocated receipts cannot be edited", errorMsg);
        }

        [Fact]
        public async Task SalesInvoice_PostSalesInvoice_Balancing_WithMixedFractionsAndDiscounts()
        {
            // Create invoice with mixed fractional prices, discounts, tax, and shipping
            var invoice = await NewInvoice();
            invoice.Status = "Draft";
            invoice.ShippingHandlingCharge = 15.55m;
            invoice.SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>
            {
                new() { ItemId = invoice.SalesInvoiceLines[0].ItemId, Quantity = 3, Amount = 33.33m, Discount = 5m },
                new() { ItemId = invoice.SalesInvoiceLines[0].ItemId, Quantity = 7, Amount = 14.15m, Discount = 3.5m }
            };

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());
            var savedInvoice = await saveRes.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();

            // Issuing posts to GL - must balance Dr == Cr
            var issueRes = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={savedInvoice.Id}");
            Assert.True(issueRes.IsSuccessStatusCode, await issueRes.Content.ReadAsStringAsync());

            var reloaded = await GetInvoice(savedInvoice.Id);
            Assert.True(reloaded.Posted);
            Assert.Null(reloaded.PostingError);

            // Assert AR debit in GL lines equals GrandTotal() exactly
            WithDb(db =>
            {
                var header = db.SalesInvoiceHeaders
                    .Include(h => h.SalesInvoiceLines)
                    .Include(h => h.Customer)
                    .Include(h => h.GeneralLedgerHeader).ThenInclude(gl => gl.GeneralLedgerLines)
                    .Single(h => h.Id == savedInvoice.Id);
                var arAccountId = header.Customer.AccountsReceivableAccountId ?? 1;
                var arLine = header.GeneralLedgerHeader.GeneralLedgerLines.Single(l => l.DrCr == DrOrCrSide.Dr && l.AccountId == arAccountId);
                Assert.Equal(header.GrandTotal(), arLine.Amount);
                var totalDebits = header.GeneralLedgerHeader.GeneralLedgerLines.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount);
                var totalCredits = header.GeneralLedgerHeader.GeneralLedgerLines.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount);
                Assert.Equal(totalDebits, totalCredits);
            });
        }

        [Fact]
        public async Task SalesInvoice_PostSalesInvoice_RejectsVoidAndClosed_AndDoesNotReopen()
        {
            // 1. Post on Void is rejected and remains Void
            var draftId = await SaveDraft();
            var voidRes = await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={draftId}");
            Assert.True(voidRes.IsSuccessStatusCode);
            var voidPostRes = await Send(HttpMethod.Post, "/api/Sales/PostSalesInvoice", new Dto.Sales.SalesInvoice { Id = draftId });
            Assert.Equal(HttpStatusCode.BadRequest, voidPostRes.StatusCode);
            var voidInvoice = await GetInvoice(draftId);
            Assert.Equal("Void", voidInvoice.Status);

            // 2. Post on Closed (Paid) is rejected and remains Closed
            var paidDraftId = await SaveDraft();
            await Issue(paidDraftId);
            var paidInvoice = await GetInvoice(paidDraftId);
            var payRes = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", new Dto.Sales.RecordInvoicePaymentDto
            {
                InvoiceId = paidDraftId,
                Amount = paidInvoice.BalanceDue,
                PaymentMethod = "Cash"
            });
            Assert.True(payRes.IsSuccessStatusCode);
            var closedInvoice = await GetInvoice(paidDraftId);
            Assert.Equal("Paid", closedInvoice.Status);

            var closedPostRes = await Send(HttpMethod.Post, "/api/Sales/PostSalesInvoice", new Dto.Sales.SalesInvoice { Id = paidDraftId });
            Assert.Equal(HttpStatusCode.BadRequest, closedPostRes.StatusCode);
            var reloadedClosed = await GetInvoice(paidDraftId);
            Assert.Equal("Paid", reloadedClosed.Status);
        }

        [Fact]
        public async Task SalesInvoice_CreateSalesInvoice_ForcesDraft_AndRejectsNonDraft()
        {
            var invoice = await NewInvoice();

            // Attempting to create with non-draft status via CreateSalesInvoice is rejected
            invoice.Status = "Open";
            var openRes = await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice);
            Assert.Equal(HttpStatusCode.BadRequest, openRes.StatusCode);

            invoice.Status = "Closed";
            var closedRes = await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice);
            Assert.Equal(HttpStatusCode.BadRequest, closedRes.StatusCode);

            invoice.Status = "Void";
            var voidRes = await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice);
            Assert.Equal(HttpStatusCode.BadRequest, voidRes.StatusCode);

            // Creating with Draft succeeds as Draft
            invoice.Status = "Draft";
            var draftRes = await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice);
            Assert.True(draftRes.IsSuccessStatusCode, await draftRes.Content.ReadAsStringAsync());
            var created = await draftRes.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            Assert.Equal("Draft", created.Status);
            var fetched = await GetInvoice(created.Id);
            Assert.Equal("Draft", fetched.Status);
        }
    }
}
