#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests
{
    public class MultiTenancyPhase2Tests : IAsyncLifetime
    {
        private readonly string _dbPath;
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _client = null!;

        public int CompanyAId { get; private set; }
        public int CompanyBId { get; private set; }
        public string SlugA => "alpha";
        public string SlugB => "beta";

        public string TokenUA { get; private set; } = string.Empty;
        public string TokenUB { get; private set; } = string.Empty;
        public string TokenUAB { get; private set; } = string.Empty;

        public string UserAId { get; private set; } = string.Empty;
        public string UserBId { get; private set; } = string.Empty;
        public string UserABId { get; private set; } = string.Empty;

        public int CustomerAId { get; private set; }
        public int CustomerBId { get; private set; }
        public int ItemAId { get; private set; }
        public int ItemBId { get; private set; }
        public int AccountAId { get; private set; }
        public int AccountBId { get; private set; }
        public int JournalEntryAId { get; private set; }
        public int JournalEntryBId { get; private set; }
        public int DonorAId { get; private set; }
        public int DonorBId { get; private set; }

        public MultiTenancyPhase2Tests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"accountgo_p2_{Guid.NewGuid():N}.db");
        }

        public async Task InitializeAsync()
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={_dbPath}");
                builder.UseSetting("ENVIRONMENT", "Development");
                builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            });

            _client = _factory.CreateClient();

            // Ensure app is booted
            var health = await _client.GetAsync("/health");
            Assert.True(health.IsSuccessStatusCode, "Failed to boot app for Phase 2 tests.");

            await SeedCompaniesAndUsersAsync();
        }

        public Task DisposeAsync()
        {
            _client.Dispose();
            _factory.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
            return Task.CompletedTask;
        }

        private async Task SeedCompaniesAndUsersAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            // 1. Create Companies A and B
            var companyA = new Company
            {
                Name = "Alpha Corporation",
                ShortName = "Alpha",
                Slug = SlugA,
                Plan = "enterprise",
                SubscriptionStatus = "active"
            };
            var companyB = new Company
            {
                Name = "Beta Enterprise",
                ShortName = "Beta",
                Slug = SlugB,
                Plan = "enterprise",
                SubscriptionStatus = "active"
            };
            db.Companies.AddRange(companyA, companyB);
            await db.SaveChangesAsync();

            CompanyAId = companyA.Id;
            CompanyBId = companyB.Id;

            // 2. Create Users UA, UB, UAB
            var userA = new ApplicationUser { UserName = "ua@test.com", Email = "ua@test.com", EmailConfirmed = true };
            var userB = new ApplicationUser { UserName = "ub@test.com", Email = "ub@test.com", EmailConfirmed = true };
            var userAB = new ApplicationUser { UserName = "uab@test.com", Email = "uab@test.com", EmailConfirmed = true };

            await userMgr.CreateAsync(userA, "Password123!");
            await userMgr.CreateAsync(userB, "Password123!");
            await userMgr.CreateAsync(userAB, "Password123!");

            UserAId = userA.Id;
            UserBId = userB.Id;
            UserABId = userAB.Id;

            // 3. Create Memberships
            db.CompanyMembers.AddRange(
                new CompanyMember
                {
                    CompanyId = CompanyAId,
                    UserId = userA.Id,
                    Role = CompanyMemberRole.Owner,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                },
                new CompanyMember
                {
                    CompanyId = CompanyBId,
                    UserId = userB.Id,
                    Role = CompanyMemberRole.Owner,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                },
                new CompanyMember
                {
                    CompanyId = CompanyAId,
                    UserId = userAB.Id,
                    Role = CompanyMemberRole.Owner,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                },
                new CompanyMember
                {
                    CompanyId = CompanyBId,
                    UserId = userAB.Id,
                    Role = CompanyMemberRole.Owner,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                }
            );
            await db.SaveChangesAsync();

            // 4. Generate JWT tokens
            var tokenA = await authService.CreateToken(userA, true);
            var tokenB = await authService.CreateToken(userB, true);
            var tokenAB = await authService.CreateToken(userAB, true);

            TokenUA = tokenA.AccessToken;
            TokenUB = tokenB.AccessToken;
            TokenUAB = tokenAB.AccessToken;

            // 5. Seed entities for Company A
            using (tenantCtx.Impersonate(CompanyAId, SlugA, CompanyMemberRole.Owner))
            {
                var custA = new Customer
                {
                    No = "CUST-A",
                    Party = new Party { Name = "Customer Alpha", PartyType = PartyTypes.Customer },
                    PrimaryContact = new Contact
                    {
                        FirstName = "Alice",
                        LastName = "Alpha",
                        Party = new Party { Name = "Alice Alpha", PartyType = PartyTypes.Contact }
                    }
                };
                var accA = new Account
                {
                    AccountCode = "10111",
                    AccountName = "Cash in Bank A",
                    AccountClassId = 1,
                    IsCash = true
                };
                var itemA = new Item
                {
                    Code = "ITEM-A",
                    Description = "Item A for Alpha",
                    Price = 100,
                    Cost = 50,
                    ItemCategoryId = 1,
                    SmallestMeasurementId = 1
                };
                var jeA = new JournalEntryHeader
                {
                    Date = DateTime.UtcNow,
                    VoucherType = JournalVoucherTypes.Other,
                    Memo = "JE Alpha",
                    ReferenceNo = "JE-A-01"
                };

                try { db.Customers.Add(custA); await db.SaveChangesAsync(); } catch (Exception ex) { throw new Exception("Failed saving custA: " + ex); }
                try { db.Accounts.Add(accA); await db.SaveChangesAsync(); } catch (Exception ex) { throw new Exception("Failed saving accA: " + ex); }
                try { db.Items.Add(itemA); await db.SaveChangesAsync(); } catch (Exception ex) { throw new Exception("Failed saving itemA: " + ex); }
                try { db.JournalEntryHeaders.Add(jeA); await db.SaveChangesAsync(); } catch (Exception ex) { throw new Exception("Failed saving jeA: " + ex); }

                CustomerAId = custA.Id;
                AccountAId = accA.Id;
                ItemAId = itemA.Id;
                JournalEntryAId = jeA.Id;

                var donorA = NewDonor("alpha.donor@test.com");
                db.Donors.Add(donorA);
                await db.SaveChangesAsync();
                DonorAId = donorA.Id;
            }

            // 6. Seed entities for Company B
            using (tenantCtx.Impersonate(CompanyBId, SlugB, CompanyMemberRole.Owner))
            {
                var custB = new Customer
                {
                    No = "CUST-B",
                    Party = new Party { Name = "Customer Beta", PartyType = PartyTypes.Customer },
                    PrimaryContact = new Contact
                    {
                        FirstName = "Bob",
                        LastName = "Beta",
                        Party = new Party { Name = "Bob Beta", PartyType = PartyTypes.Contact }
                    }
                };
                var accB = new Account
                {
                    AccountCode = "10112",
                    AccountName = "Cash in Bank B",
                    AccountClassId = 1,
                    IsCash = true
                };
                var itemB = new Item
                {
                    Code = "ITEM-B",
                    Description = "Item B for Beta",
                    Price = 200,
                    Cost = 100,
                    ItemCategoryId = 1,
                    SmallestMeasurementId = 1
                };
                var jeB = new JournalEntryHeader
                {
                    Date = DateTime.UtcNow,
                    VoucherType = JournalVoucherTypes.Other,
                    Memo = "JE Beta",
                    ReferenceNo = "JE-B-01"
                };

                db.Customers.Add(custB);
                db.Accounts.Add(accB);
                db.Items.Add(itemB);
                db.JournalEntryHeaders.Add(jeB);
                await db.SaveChangesAsync();

                CustomerBId = custB.Id;
                AccountBId = accB.Id;
                ItemBId = itemB.Id;
                JournalEntryBId = jeB.Id;

                var donorB = NewDonor("beta.donor@test.com");
                db.Donors.Add(donorB);
                await db.SaveChangesAsync();
                DonorBId = donorB.Id;
            }
        }

        private static Core.Domain.Donations.Donor NewDonor(string email) => new()
        {
            DonorType = "Individual",
            FirstName = "Dana",
            LastName = "Donor",
            Street = "1 Main St",
            Province = "ON",
            PostalCode = "K1A0B1",
            Telephone = "5555555555",
            Email = email
        };

        private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, string token, string? orgSlug, object? body = null)
        {
            var req = CreateRequest(method, path, token, orgSlug);
            if (body != null)
                req.Content = JsonContent.Create(body);
            return await _client.SendAsync(req);
        }

        private static async Task AssertCrossTenantReferenceDeniedAsync(HttpResponseMessage res)
        {
            var body = await res.Content.ReadAsStringAsync();
            Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)res.StatusCode}: {body}");
            Assert.Contains("CROSS_TENANT_REFERENCE_DENIED", body);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path, string token, string? orgSlug = null)
        {
            var req = new HttpRequestMessage(method, path);
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(orgSlug))
                req.Headers.Add("X-AccountGo-Org", orgSlug);
            return req;
        }

        // Test 1: Seed companies A & B, users UA (member of A only) & UB (member of B only)
        [Fact]
        public async Task Test1_Seed_Companies_And_Users_Isolation()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

            var membersA = await db.CompanyMembers.Where(m => m.CompanyId == CompanyAId).ToListAsync();
            var membersB = await db.CompanyMembers.Where(m => m.CompanyId == CompanyBId).ToListAsync();

            Assert.Contains(membersA, m => m.UserId == UserAId);
            Assert.DoesNotContain(membersA, m => m.UserId == UserBId);

            Assert.Contains(membersB, m => m.UserId == UserBId);
            Assert.DoesNotContain(membersB, m => m.UserId == UserAId);
        }

        // Test 2: UA lists customers/invoices/accounts/items/journal entries with X-AccountGo-Org: a → sees only A's rows
        [Fact]
        public async Task Test2_UA_Lists_Entities_With_HeaderA_Sees_Only_A_Rows()
        {
            // Customers
            var custReq = CreateRequest(HttpMethod.Get, "/api/Sales/Customers", TokenUA, SlugA);
            var custRes = await _client.SendAsync(custReq);
            Assert.Equal(HttpStatusCode.OK, custRes.StatusCode);
            var custJson = await custRes.Content.ReadAsStringAsync();
            Assert.Contains("Customer Alpha", custJson);
            Assert.DoesNotContain("Customer Beta", custJson);

            // Items
            var itemReq = CreateRequest(HttpMethod.Get, "/api/Catalog/Items", TokenUA, SlugA);
            var itemRes = await _client.SendAsync(itemReq);
            Assert.Equal(HttpStatusCode.OK, itemRes.StatusCode);
            var itemJson = await itemRes.Content.ReadAsStringAsync();
            Assert.Contains("Item A for Alpha", itemJson);
            Assert.DoesNotContain("Item B for Beta", itemJson);

            // Accounts
            var accReq = CreateRequest(HttpMethod.Get, "/api/Financials/Accounts", TokenUA, SlugA);
            var accRes = await _client.SendAsync(accReq);
            Assert.Equal(HttpStatusCode.OK, accRes.StatusCode);
            var accJson = await accRes.Content.ReadAsStringAsync();
            Assert.Contains("Cash in Bank A", accJson);
            Assert.DoesNotContain("Cash in Bank B", accJson);

            // Journal Entries
            var jeReq = CreateRequest(HttpMethod.Get, "/api/Financials/JournalEntries", TokenUA, SlugA);
            var jeRes = await _client.SendAsync(jeReq);
            Assert.Equal(HttpStatusCode.OK, jeRes.StatusCode);
            var jeJson = await jeRes.Content.ReadAsStringAsync();
            Assert.Contains("JE Alpha", jeJson);
            Assert.DoesNotContain("JE Beta", jeJson);
        }

        // Test 3: UA with X-AccountGo-Org: b → 404 on every tenant endpoint. Same 404 for a non-existent slug
        [Fact]
        public async Task Test3_UA_With_HeaderB_Or_NonExistent_Returns_404_No_Existence_Oracle()
        {
            var reqB = CreateRequest(HttpMethod.Get, "/api/Sales/Customers", TokenUA, SlugB);
            var resB = await _client.SendAsync(reqB);
            Assert.Equal(HttpStatusCode.NotFound, resB.StatusCode);
            var bodyB = await resB.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(bodyB);
            Assert.Equal("Workspace not found", bodyB.Title);

            var reqNonExistent = CreateRequest(HttpMethod.Get, "/api/Sales/Customers", TokenUA, "random-unknown-org");
            var resNonExistent = await _client.SendAsync(reqNonExistent);
            Assert.Equal(HttpStatusCode.NotFound, resNonExistent.StatusCode);
            var bodyNonExistent = await resNonExistent.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(bodyNonExistent);
            Assert.Equal("Workspace not found", bodyNonExistent.Title);

            Assert.Equal(bodyB.Title, bodyNonExistent.Title);
            Assert.Equal(bodyB.Status, bodyNonExistent.Status);
        }

        // Test 4: UA GET /api/Sales/customer/{idOfB} with header A → 404
        [Fact]
        public async Task Test4_UA_Single_Entity_Of_Other_Tenant_Returns_404()
        {
            // Customer
            var reqCust = CreateRequest(HttpMethod.Get, $"/api/Sales/Customer?id={CustomerBId}", TokenUA, SlugA);
            var resCust = await _client.SendAsync(reqCust);
            Assert.Equal(HttpStatusCode.NotFound, resCust.StatusCode);

            // Item
            var reqItem = CreateRequest(HttpMethod.Get, $"/api/Catalog/Item?id={ItemBId}", TokenUA, SlugA);
            var resItem = await _client.SendAsync(reqItem);
            Assert.Equal(HttpStatusCode.NotFound, resItem.StatusCode);

            // Account
            var reqAcc = CreateRequest(HttpMethod.Get, $"/api/Financials/Account?id={AccountBId}", TokenUA, SlugA);
            var resAcc = await _client.SendAsync(reqAcc);
            Assert.Equal(HttpStatusCode.NotFound, resAcc.StatusCode);

            // Journal Entry
            var reqJe = CreateRequest(HttpMethod.Get, $"/api/Financials/JournalEntry?id={JournalEntryBId}", TokenUA, SlugA);
            var resJe = await _client.SendAsync(reqJe);
            Assert.Equal(HttpStatusCode.NotFound, resJe.StatusCode);
        }

        // Test 5: UA creates an invoice in A referencing B's customer id / item id / account id → 400/404, and no row is written
        [Fact]
        public async Task Test5_Cross_Tenant_Reference_Rejected_No_Row_Written()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using (tenantCtx.Impersonate(CompanyAId, SlugA, CompanyMemberRole.Owner))
                {
                    var countBefore = await db.SalesInvoiceHeaders.CountAsync();

                    // Reference Customer B
                    var invoiceWithCustB = new Dto.Sales.SalesInvoice
                    {
                        CustomerId = CustomerBId,
                        InvoiceDate = DateTime.UtcNow,
                        SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>
                        {
                            new Dto.Sales.SalesInvoiceLine { ItemId = ItemAId, Amount = 100, Quantity = 1 }
                        }
                    };

                    var req1 = CreateRequest(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", TokenUA, SlugA);
                    req1.Content = JsonContent.Create(invoiceWithCustB);
                    var res1 = await _client.SendAsync(req1);
                    await AssertCrossTenantReferenceDeniedAsync(res1);

                    // Reference Item B
                    var invoiceWithItemB = new Dto.Sales.SalesInvoice
                    {
                        CustomerId = CustomerAId,
                        InvoiceDate = DateTime.UtcNow,
                        SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>
                        {
                            new Dto.Sales.SalesInvoiceLine { ItemId = ItemBId, Amount = 100, Quantity = 1 }
                        }
                    };

                    var req2 = CreateRequest(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", TokenUA, SlugA);
                    req2.Content = JsonContent.Create(invoiceWithItemB);
                    var res2 = await _client.SendAsync(req2);
                    await AssertCrossTenantReferenceDeniedAsync(res2);

                    // Same references through the service-based create/update endpoints
                    await AssertCrossTenantReferenceDeniedAsync(
                        await SendJsonAsync(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", TokenUA, SlugA, invoiceWithCustB));
                    await AssertCrossTenantReferenceDeniedAsync(
                        await SendJsonAsync(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", TokenUA, SlugA, invoiceWithItemB));
                    await AssertCrossTenantReferenceDeniedAsync(
                        await SendJsonAsync(HttpMethod.Post, "/api/Sales/UpdateSalesInvoice", TokenUA, SlugA, invoiceWithCustB));

                    // Assert no row was written
                    var countAfter = await db.SalesInvoiceHeaders.CountAsync();
                    Assert.Equal(countBefore, countAfter);

                    // Account: parent account from B
                    var accountsBefore = await db.Accounts.CountAsync();
                    await AssertCrossTenantReferenceDeniedAsync(
                        await SendJsonAsync(HttpMethod.Post, "/api/Financials/AddAccount", TokenUA, SlugA,
                            new Dto.Financial.Account { AccountCode = "19999", AccountName = "Child of B", ParentAccountId = AccountBId }));
                    Assert.Equal(accountsBefore, await db.Accounts.CountAsync());

                    // Donation: donor from B, and GL account from B
                    var donationsBefore = await db.DonationInvoiceHeaders.CountAsync();
                    await AssertCrossTenantReferenceDeniedAsync(
                        await SendJsonAsync(HttpMethod.Post, "/api/Donations/CreateDonationInvoice", TokenUA, SlugA,
                            new Dto.Donations.DonationInvoice { DonorId = DonorBId, Amount = 10, PaymentType = "Cash", GlAccountId = AccountAId }));
                    await AssertCrossTenantReferenceDeniedAsync(
                        await SendJsonAsync(HttpMethod.Post, "/api/Donations/CreateDonationInvoice", TokenUA, SlugA,
                            new Dto.Donations.DonationInvoice { DonorId = DonorAId, Amount = 10, PaymentType = "Cash", GlAccountId = AccountBId }));
                    Assert.Equal(donationsBefore, await db.DonationInvoiceHeaders.CountAsync());
                }
            }
        }

        // Test 6: Created rows have CompanyId = A
        [Fact]
        public async Task Test6_Created_Rows_Stamped_With_CompanyId()
        {
            var customerDto = new Dto.Sales.Customer
            {
                Name = "Brand New Customer Alpha",
                No = $"CUST-{Guid.NewGuid():N}".Substring(0, 8)
            };

            var req = CreateRequest(HttpMethod.Post, "/api/Sales/SaveCustomer", TokenUA, SlugA);
            req.Content = JsonContent.Create(customerDto);
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            // Verify in DB that CompanyId was stamped to CompanyAId
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using (tenantCtx.Impersonate(CompanyAId, SlugA, CompanyMemberRole.Owner))
            {
                var created = await db.Customers.Include(c => c.Party).FirstOrDefaultAsync(c => c.Party.Name == "Brand New Customer Alpha");
                Assert.NotNull(created);
                Assert.Equal(CompanyAId, created.CompanyId);
            }
        }

        // Test 7: Tenant-scoped endpoint without header → 400. Unauthenticated → 401
        [Fact]
        public async Task Test7_Tenant_Scoped_Missing_Header_Returns_400_Unauthenticated_Returns_401()
        {
            // Missing header -> 400
            var reqNoHeader = CreateRequest(HttpMethod.Get, "/api/Sales/Customers", TokenUA, orgSlug: null);
            var resNoHeader = await _client.SendAsync(reqNoHeader);
            Assert.Equal(HttpStatusCode.BadRequest, resNoHeader.StatusCode);
            var bodyNoHeader = await resNoHeader.Content.ReadAsStringAsync();
            Assert.Contains("X-AccountGo-Org header required", bodyNoHeader);

            // Unauthenticated -> 401
            var reqUnauth = CreateRequest(HttpMethod.Get, "/api/Sales/Customers", token: string.Empty, SlugA);
            var resUnauth = await _client.SendAsync(reqUnauth);
            Assert.Equal(HttpStatusCode.Unauthorized, resUnauth.StatusCode);
        }

        // Test 8: Reflection test: every controller action has [TenantScoped] or [TenantFree]
        [Fact]
        public void Test8_Reflection_Every_Controller_Action_Has_TenantScoped_Or_TenantFree()
        {
            var controllerTypes = typeof(Program).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            foreach (var controller in controllerTypes)
            {
                var controllerHasFree = controller.GetCustomAttribute<TenantFreeAttribute>() != null;
                var controllerHasScoped = controller.GetCustomAttribute<TenantScopedAttribute>() != null;

                var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => !m.IsSpecialName && m.DeclaringType == controller)
                    .ToList();

                foreach (var action in actions)
                {
                    var actionHasFree = action.GetCustomAttribute<TenantFreeAttribute>() != null || controllerHasFree;
                    var actionHasScoped = action.GetCustomAttribute<TenantScopedAttribute>() != null || controllerHasScoped;

                    Assert.True(actionHasFree ^ actionHasScoped,
                        $"Action {controller.Name}.{action.Name} must have exactly one of [TenantScoped] or [TenantFree].");
                }
            }
        }

        // Test 9: Grep test: IgnoreQueryFilters only in the allowed files
        [Fact]
        public void Test9_IgnoreQueryFilters_Only_In_Allowed_Files()
        {
            var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "InternalSyncController.cs",
                "SqliteSchemaUpgrader.cs",
                "CompanyProvisioningService.cs"
            };

            var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var srcDir = Path.Combine(solutionRoot, "src");

            var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();

            var offendingFiles = new List<string>();

            foreach (var file in csFiles)
            {
                var fileName = Path.GetFileName(file);
                if (allowedFiles.Contains(fileName))
                    continue;

                var content = File.ReadAllText(file);
                if (content.Contains("IgnoreQueryFilters("))
                {
                    offendingFiles.Add(fileName);
                }
            }

            Assert.Empty(offendingFiles);
        }

        // Test 10: Module gating via owner opt-outs (community plan includes all modules).
        [Fact]
        public async Task Test10_Module_Gating_Enforced_Per_Workspace_Plan()
        {
            try
            {
                using (var scope = _factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                    var compA = await db.Companies.FirstAsync(c => c.Id == CompanyAId);
                    compA.Plan = "community";
                    // Class-level gate is any-of financials|reports — opt out both.
                    compA.DisabledModulesJson = "[\"financials\",\"reports\"]";
                    compA.ActiveModulesJson = null;
                    var compB = await db.Companies.FirstAsync(c => c.Id == CompanyBId);
                    compB.Plan = "community";
                    compB.DisabledModulesJson = "[]";
                    compB.ActiveModulesJson = null;
                    await db.SaveChangesAsync();
                }

                // Company A opted out of financials -> 403 MODULE_NOT_SUBSCRIBED
                var reqA = CreateRequest(HttpMethod.Get, "/api/Financials/Accounts", TokenUAB, SlugA);
                var resA = await _client.SendAsync(reqA);
                Assert.Equal(HttpStatusCode.Forbidden, resA.StatusCode);
                var bodyA = await resA.Content.ReadAsStringAsync();
                Assert.Contains("MODULE_NOT_SUBSCRIBED", bodyA);

                // Company B keeps financials -> 200 OK
                var reqB = CreateRequest(HttpMethod.Get, "/api/Financials/Accounts", TokenUAB, SlugB);
                var resB = await _client.SendAsync(reqB);
                Assert.Equal(HttpStatusCode.OK, resB.StatusCode);
            }
            finally
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var compA = await db.Companies.FirstAsync(c => c.Id == CompanyAId);
                compA.Plan = "community";
                compA.DisabledModulesJson = "[]";
                var compB = await db.Companies.FirstAsync(c => c.Id == CompanyBId);
                compB.Plan = "community";
                compB.DisabledModulesJson = "[]";
                await db.SaveChangesAsync();
            }
        }

        // Test 11: GET /api/organizations/current returns active workspace info
        [Fact]
        public async Task Test11_Organizations_Current_Endpoint_Returns_Expected_Payload()
        {
            var req = CreateRequest(HttpMethod.Get, "/api/organizations/current", TokenUA, SlugA);
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var json = await res.Content.ReadAsStringAsync();
            Assert.Contains("\"slug\":\"alpha\"", json);
            Assert.Contains("\"role\":\"Owner\"", json);
            Assert.Contains("\"plan\":\"community\"", json);
        }
        // Old endpoints that listed or described every workspace are gone or scoped to the caller's workspace.
        [Fact]
        public async Task Test12_Workspace_Listing_And_Subscription_Do_Not_Leak_Other_Workspaces()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, "/api/Subscription/companies", TokenUA, null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, "/api/Administration/companies", TokenUA, SlugA)).StatusCode);

            // ?slug= is ignored: the answer is always the header's workspace.
            var sub = await SendJsonAsync(HttpMethod.Get, $"/api/Subscription/current?slug={SlugB}", TokenUA, SlugA);
            Assert.Equal(HttpStatusCode.OK, sub.StatusCode);
            var subJson = await sub.Content.ReadAsStringAsync();
            Assert.Contains(SlugA, subJson);
            Assert.DoesNotContain("Beta Enterprise", subJson);

            // GET /api/organizations lists only the caller's memberships, and needs no header.
            var orgs = await SendJsonAsync(HttpMethod.Get, "/api/organizations", TokenUA, null);
            Assert.Equal(HttpStatusCode.OK, orgs.StatusCode);
            var orgsJson = await orgs.Content.ReadAsStringAsync();
            Assert.Contains("Alpha Corporation", orgsJson);
            Assert.DoesNotContain("Beta Enterprise", orgsJson);
        }

        // Global user CRUD is retired; remaining role-template/platform operations are staff-only.
        [Fact]
        public async Task Test13_Global_Admin_Endpoints_Forbidden_For_Workspace_Owner()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, "/api/Administration/users", TokenUA, SlugA)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(HttpMethod.Get, "/api/Administration/GetUser?username=ub@test.com", TokenUA, SlugA)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(HttpMethod.Post, "/api/Administration/clear", TokenUA, SlugA, new { confirmation = "DELETE_ALL_DATA" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Post, "/api/Administration/users", TokenUA, SlugA, new { email = "x@test.com" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendJsonAsync(HttpMethod.Delete, "/api/Administration/roles/1", TokenUA, SlugA)).StatusCode);
        }

        // Creating a workspace makes the caller its Owner, so it is reachable with its own header right away.
        [Fact]
        public async Task Test14_Create_Organization_Makes_Caller_Owner()
        {
            var slug = $"gamma-{Guid.NewGuid():N}".Substring(0, 14);
            var created = await SendJsonAsync(HttpMethod.Post, "/api/organizations", TokenUB, null, new { name = "Gamma Works", slug });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var current = await SendJsonAsync(HttpMethod.Get, "/api/organizations/current", TokenUB, slug);
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Contains("\"role\":\"Owner\"", await current.Content.ReadAsStringAsync());

            // Another user is not a member of it.
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, "/api/organizations/current", TokenUA, slug)).StatusCode);
        }

        // DonorsController loads by id with FindAsync; the query filter must apply there too.
        [Fact]
        public async Task Test15_Donor_Of_Other_Workspace_Cannot_Be_Read_Updated_Or_Deleted()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Get, $"/api/Donors/{DonorBId}", TokenUA, SlugA)).StatusCode);
            var update = new Dto.Donors.Donor
            {
                DonorType = "Individual", FirstName = "Hijack", LastName = "Attempt", Street = "1 Main St",
                Province = "ON", PostalCode = "K1A0B1", Telephone = "5555555555", Email = "x@test.com"
            };
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Put, $"/api/Donors/{DonorBId}", TokenUA, SlugA, update)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendJsonAsync(HttpMethod.Delete, $"/api/Donors/{DonorBId}", TokenUA, SlugA)).StatusCode);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(CompanyBId, SlugB))
            {
                var donorB = await db.Donors.SingleAsync(d => d.Id == DonorBId);
                Assert.Equal("Dana", donorB.FirstName);
            }
        }
    }
}
