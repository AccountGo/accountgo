#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Entitlements;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    /// <summary>Free-tier limits: off by default, enforced server-side at every create path when enabled.</summary>
    public partial class MultiTenancyPhase3Tests
    {
        private EntitlementsOptions Limits => _factory.Services.GetRequiredService<EntitlementsOptions>();

        /// <summary>Turns limits on for one test; the options are a singleton shared by the test server.</summary>
        private IDisposable EnableFreeTier(int salesDocuments = 2, int bills = 2, int seats = 1, int banks = 1, int ownedWorkspaces = 1,
            params string[] selfServicePlans)
        {
            var options = Limits;
            options.Enabled = true;
            options.SupportEmail = "support@example.com";
            options.SelfServicePlans = selfServicePlans.Length > 0 ? selfServicePlans.ToList() : new() { "invoicing", "accounting" };
            options.FreeLimits[MeteredResource.SalesDocuments] = salesDocuments;
            options.FreeLimits[MeteredResource.Bills] = bills;
            options.FreeLimits[MeteredResource.Seats] = seats;
            options.FreeLimits[MeteredResource.BankAccounts] = banks;
            options.FreeLimits[MeteredResource.OwnedWorkspaces] = ownedWorkspaces;
            return new Restore(() =>
            {
                options.Enabled = false;
                options.SelfServicePlans.Clear();
                options.FreeLimits.Clear();
                options.SupportEmail = null;
            });
        }

        private void SetTier(string tier)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.Companies.Single(c => c.Id == _companyId).Tier = tier;
            db.SaveChanges();
        }

        private void SetPlan(string plan)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.Companies.Single(c => c.Id == _companyId).Plan = plan;
            db.SaveChanges();
        }

        private static async Task AssertQuotaExceeded(HttpResponseMessage response, string resource, int limit)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, body);
            var problem = await Json(response);
            Assert.Equal("QUOTA_EXCEEDED", problem.GetProperty("code").GetString());
            Assert.Equal(resource, problem.GetProperty("resource").GetString());
            Assert.Equal(limit, problem.GetProperty("limit").GetInt32());
            // E2: upgradeAvailable is true when AllowedTransitions includes a paid plan that lifts quotas
            // (was hard-coded false). Self-host (Enabled=false) still yields false.
            Assert.True(problem.GetProperty("upgradeAvailable").GetBoolean());
            Assert.Equal("support@example.com", problem.GetProperty("contact").GetString());
            Assert.True(problem.TryGetProperty("canManageBilling", out _));
            Assert.True(problem.TryGetProperty("monthly", out _));
            Assert.True(problem.TryGetProperty("effectivePlan", out _));
        }

        private async Task<HttpResponseMessage> SaveQuote()
        {
            var invoice = await NewInvoice();
            var line = invoice.SalesInvoiceLines.Single();
            return await Send(HttpMethod.Post, "/api/Sales/SaveQuotation", new
            {
                customerId = invoice.CustomerId,
                quotationDate = DateTime.UtcNow,
                salesQuotationLines = new[] { new { itemId = line.ItemId, measurementId = line.MeasurementId, quantity = 1, amount = 100 } }
            });
        }

        [Fact]
        public async Task Entitlements_Disabled_ByDefault_NothingIsLimitedOrRecorded()
        {
            Assert.False(Limits.Enabled);
            for (var i = 0; i < 3; i++)
                Assert.True((await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice())).IsSuccessStatusCode);

            var state = await Json(await Send(HttpMethod.Get, "/api/entitlements"));
            Assert.False(state.GetProperty("enabled").GetBoolean());
            Assert.False(state.GetProperty("limited").GetBoolean());
            Assert.Equal(Core.Domain.Company.KnownPlans.Count, state.GetProperty("selfServicePlans").GetArrayLength());
            Assert.Equal(JsonValueKindNull, state.GetProperty("limits").GetProperty("salesDocuments").GetProperty("limit").ValueKind);
            Assert.Equal(JsonValueKind.Array, state.GetProperty("features").ValueKind);
            Assert.Equal(0, state.GetProperty("features").GetArrayLength());

            using var scope = _factory.Services.CreateScope();
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                Assert.Empty(scope.ServiceProvider.GetRequiredService<ApiDbContext>().EntitlementUsages);
        }

        [Fact]
        public async Task FreeTier_SalesDocuments_InvoicesAndQuotesShareTheMonthlyLimit()
        {
            using var _ = EnableFreeTier(salesDocuments: 2);

            Assert.True((await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice())).IsSuccessStatusCode);
            var quote = await SaveQuote();
            Assert.True(quote.IsSuccessStatusCode, await quote.Content.ReadAsStringAsync());

            await AssertQuotaExceeded(await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice()), "salesDocuments", 2);
            await AssertQuotaExceeded(await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", await NewInvoice()), "salesDocuments", 2);
            await AssertQuotaExceeded(await SaveQuote(), "salesDocuments", 2);

            var state = await Json(await Send(HttpMethod.Get, "/api/entitlements"));
            Assert.True(state.GetProperty("limited").GetBoolean());
            var sales = state.GetProperty("limits").GetProperty("salesDocuments");
            Assert.Equal(2, sales.GetProperty("limit").GetInt32());
            Assert.Equal(2, sales.GetProperty("used").GetInt32());
            Assert.Equal("month", sales.GetProperty("period").GetString());

            // A blocked invoice leaves nothing behind.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                Assert.Single(db.SalesInvoiceHeaders);
                Assert.Single(db.SalesOrderHeaders);
            }
        }

        [Fact]
        public async Task FreeTier_EditingExistingDocumentsIsNeverBlocked()
        {
            using var _ = EnableFreeTier(salesDocuments: 1, bills: 1);
            var created = await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice());
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
            var invoice = await created.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            invoice.ReferenceNo = "edited";
            var updated = await Send(HttpMethod.Post, "/api/Sales/UpdateSalesInvoice", invoice);
            Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Limits vendor" });
            var bill = await SaveBillOk(NewBill(vendor.Id, ("Rent", 1, 100m, null)));
            bill.Notes = "edited";
            Assert.True((await SaveBill(bill)).IsSuccessStatusCode);
            await AssertQuotaExceeded(await SaveBill(NewBill(vendor.Id, ("Rent 2", 1, 100m, null))), "bills", 1);
        }

        [Fact]
        public async Task FreeTier_PaidAndCompTiersAreUnlimited()
        {
            using var _ = EnableFreeTier(salesDocuments: 0, bills: 0);
            await AssertQuotaExceeded(await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice()), "salesDocuments", 0);
            try
            {
                // P8: paid effective plan (not Tier=pro alone) and Tier=comp are unlimited.
                SetPlan("accounting_pro");
                Assert.True((await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice())).IsSuccessStatusCode, "accounting_pro");
                var paidState = await Json(await Send(HttpMethod.Get, "/api/entitlements"));
                Assert.Equal(CompanyTiers.Pro, paidState.GetProperty("tier").GetString());
                Assert.False(paidState.GetProperty("limited").GetBoolean());

                SetPlan("accounting_free");
                SetTier(CompanyTiers.Comp);
                Assert.True((await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", await NewInvoice())).IsSuccessStatusCode, "comp");
                var compState = await Json(await Send(HttpMethod.Get, "/api/entitlements"));
                Assert.Equal(CompanyTiers.Comp, compState.GetProperty("tier").GetString());
                Assert.False(compState.GetProperty("limited").GetBoolean());
            }
            finally
            {
                SetPlan("accounting_free");
                SetTier(null);
            }
        }

        [Fact]
        public async Task FreeTier_BankAccounts_AreCounted()
        {
            using var _ = EnableFreeTier(banks: 1);
            int accountId;
            using (var scope = _factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                accountId = scope.ServiceProvider.GetRequiredService<ApiDbContext>().Accounts
                    .Where(a => string.Compare(a.AccountCode, "10100") >= 0 && string.Compare(a.AccountCode, "10999") <= 0)
                    .OrderBy(a => a.AccountCode).Select(a => a.Id).First();
            var bank = new { name = "Operating", accountNo = "001", bankName = "Bank", accountId };
            var first = await Send(HttpMethod.Post, "/api/Financials/Bank", bank);
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
            await AssertQuotaExceeded(await Send(HttpMethod.Post, "/api/Financials/Bank", bank), "bankAccounts", 1);
        }

        [Fact]
        public async Task FreeTier_Seats_OwnerAloneFillsOneSeat_AndPendingInvitesCount()
        {
            using (EnableFreeTier(seats: 1))
                await AssertQuotaExceeded(await Send(HttpMethod.Post, Current + "/invitations", new { email = "seat@example.com", role = "Member" }), "seats", 1);

            using (EnableFreeTier(seats: 2))
            {
                Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Current + "/invitations", new { email = "seat@example.com", role = "Member" })).StatusCode);
                // Re-inviting the same address replaces the pending invitation instead of taking a new seat.
                Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Current + "/invitations", new { email = "seat@example.com", role = "Member" })).StatusCode);
                await AssertQuotaExceeded(await Send(HttpMethod.Post, Current + "/invitations", new { email = "seat2@example.com", role = "Member" }), "seats", 2);
            }
        }

        [Fact]
        public async Task FreeTier_Seats_AcceptIsBlockedWhenTheWorkspaceIsAlreadyOverItsLimit()
        {
            await Invite("phase3-other@example.com");
            var token = InvitationToken();
            using var _ = EnableFreeTier(seats: 1);
            await AssertQuotaExceeded(await Send(HttpMethod.Post, $"/api/invitations/{token}/accept", token: _otherToken, scoped: false), "seats", 1);
        }

        [Fact]
        public async Task FreeTier_OwnedWorkspaces_And_SelfServicePlans()
        {
            using var _ = EnableFreeTier(ownedWorkspaces: 2);
            var plans = await Json(await Send(HttpMethod.Get, "/api/Subscription/plans", scoped: false));
            // The community catalog serves one plan and does not apply SelfServicePlans to it.
            var plan = Assert.Single(plans.EnumerateArray());
            Assert.Equal("community", plan.GetProperty("key").GetString());
            Assert.False(plan.GetProperty("selfService").GetBoolean());
            // Enterprise is not self-service on hosted.
            var enterprise = await Send(HttpMethod.Post, "/api/organizations", new { name = "Big Co", slug = "big-co", plan = "enterprise" }, scoped: false);
            Assert.Equal(HttpStatusCode.Forbidden, enterprise.StatusCode);
            Assert.Equal("PLAN_NOT_AVAILABLE", (await Json(enterprise)).GetProperty("code").GetString());
            var switchToEnterprise = await Send(HttpMethod.Put, "/api/Subscription/update", new { plan = "enterprise" });
            Assert.Equal(HttpStatusCode.Forbidden, switchToEnterprise.StatusCode);

            // Owner already owns 1; a second is allowed, a third is not.
            Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/organizations", new { name = "Second", slug = "second-ws", plan = "invoicing_free" }, scoped: false)).StatusCode);
            await AssertQuotaExceeded(await Send(HttpMethod.Post, "/api/organizations", new { name = "Third", slug = "third-ws" }, scoped: false), "ownedWorkspaces", 2);

            // Switching between self-service products is free.
            var toInvoicing = await Send(HttpMethod.Put, "/api/Subscription/update", new { plan = "invoicing" });
            Assert.True(toInvoicing.IsSuccessStatusCode, await toInvoicing.Content.ReadAsStringAsync());
            Assert.True((await Send(HttpMethod.Put, "/api/Subscription/update", new { plan = "accounting" })).IsSuccessStatusCode);
        }

        [Fact]
        public async Task FreeTier_CompWorkspace_KeepsAndCanPickAnOpsAssignedPlan()
        {
            using var _ = EnableFreeTier();
            try
            {
                SetTier(CompanyTiers.Comp);
                Assert.True((await Send(HttpMethod.Put, "/api/Subscription/update", new { plan = "enterprise" })).IsSuccessStatusCode);
                SetTier(null);
                // Staying on a non-self-service plan (toggling modules) still works once back on free.
                Assert.True((await Send(HttpMethod.Put, "/api/Subscription/update", new { plan = "enterprise", activeModules = new[] { "sales", "inventory" } })).IsSuccessStatusCode);
            }
            finally
            {
                SetTier(null);
                await Send(HttpMethod.Put, "/api/Subscription/update", new { plan = "accounting" });
            }
        }

        private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

        private sealed class Restore : IDisposable
        {
            private readonly Action _undo;
            public Restore(Action undo) => _undo = undo;
            public void Dispose() => _undo();
        }
    }
}
