#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AccountGo.BackendTests.Observability;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Sales;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.PublicIds;

/// <summary>
/// §6.7 Query budget test: verifies that public ID prefetch adds at most 1 query per distinct prefix
/// on hot endpoints (including a 100-row invoice list) with zero N+1 queries.
/// </summary>
[Collection("WorkspaceSetupHttp")]
public sealed class PublicIdQueryBudgetTests
{
    private readonly ITestOutputHelper _output;

    public PublicIdQueryBudgetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task HotEndpoints_With100Invoices_HaveNoNPlusOnePublicIdQueries()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"accountgo-pubid-budget-{Guid.NewGuid():N}.db");
        using var collector = new RequestQueryBudgetTests.RequestCommandCollector();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={sqlitePath}");
            builder.UseSetting("SEED_DEMO_DATA", "true");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        });

        try
        {
            using var client = factory.CreateClient();
            string token;
            string orgSlug;
            string sampleInvoicePublicId;
            string sampleBankPublicId;

            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<ApiDbContext>();
                collector.Database = RequestQueryBudgetTests.RequestCommandCollector.ConnectionKey(db.Database.GetDbConnection());

                var company = db.Companies.IgnoreQueryFilters().First();
                orgSlug = company.Slug!;
                ((Api.Tenancy.TenantContext)services.GetRequiredService<Core.Tenancy.ITenantContext>())
                    .SetTenant(company.Id, company.Slug!, CompanyMemberRole.Owner, Array.Empty<string>());

                var user = new ApplicationUser { UserName = "pubid-budget@example.test", Email = "pubid-budget@example.test", EmailConfirmed = true };
                Assert.True((await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded);
                var security = services.GetRequiredService<Services.Security.ISecurityService>();
                security.EnsureDefaultPermissionsAndRoles();
                security.AddUser(user.Email, user.Email, "Budget", "Tester", new List<int>());
                var member = new CompanyMember { CompanyId = company.Id, UserId = user.Id, Role = CompanyMemberRole.Owner, Status = CompanyMemberStatus.Active };
                db.CompanyMembers.Add(member);
                await db.SaveChangesAsync();
                foreach (var roleId in db.SecurityRoles.Where(r => r.Name != "SystemAdministrators").Select(r => r.Id).ToList())
                    db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });

                // Seed 100 invoices to verify O(1) batched prefetch instead of N+1
                var customer = db.Customers.IgnoreQueryFilters().First(c => c.CompanyId == company.Id);
                var item = db.Items.IgnoreQueryFilters().First(i => i.CompanyId == company.Id);
                var uom = db.Measurements.IgnoreQueryFilters().First(m => m.CompanyId == company.Id);
                var salesAccount = db.Accounts.IgnoreQueryFilters().First(a => a.CompanyId == company.Id && a.AccountCode.StartsWith("4"));

                for (var i = 1; i <= 100; i++)
                {
                    var inv = new SalesInvoiceHeader
                    {
                        CompanyId = company.Id,
                        CustomerId = customer.Id,
                        No = $"BUDGET-{i:D4}",
                        Date = DateTime.UtcNow.Date.AddDays(-i % 30),
                        Status = SalesInvoiceStatus.Draft,
                    };
                    inv.SalesInvoiceLines.Add(new SalesInvoiceLine
                    {
                        CompanyId = company.Id,
                        ItemId = item.Id,
                        MeasurementId = uom.Id,
                        Quantity = 1,
                        Amount = 100m,
                        SalesAccountId = salesAccount.Id,
                        Description = $"Line {i}"
                    });
                    db.SalesInvoiceHeaders.Add(inv);
                }
                await db.SaveChangesAsync();

                sampleInvoicePublicId = db.SalesInvoiceHeaders.IgnoreQueryFilters().First(i => i.CompanyId == company.Id).PublicId;
                sampleBankPublicId = db.Banks.IgnoreQueryFilters().First(b => b.CompanyId == company.Id).PublicId;

                token = (await services.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;
            }

            async Task<(HttpStatusCode Status, IReadOnlyList<string> Commands, string Body)> Send(string path)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("X-AccountGo-Org", orgSlug);
                var commands = new List<string>();
                collector.Current = commands;
                try
                {
                    using var response = await client.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();
                    return (response.StatusCode, commands, body);
                }
                finally { collector.Current = null; }
            }

            // Warm up
            await Send("/api/Dashboard/Summary");

            // Measure 100-row invoice list
            var invList = await Send("/api/Sales/SalesInvoices");
            Assert.Equal(HttpStatusCode.OK, invList.Status);
            var invArr = JsonNode.Parse(invList.Body) as JsonArray;
            Assert.NotNull(invArr);
            Assert.True(invArr!.Count >= 100, $"Expected >= 100 invoices, got {invArr.Count}");
            var invListPrefetchQueries = invList.Commands.Count(c => c.Contains("\"PublicId\"", StringComparison.Ordinal) && c.Contains("SELECT", StringComparison.OrdinalIgnoreCase) && c.Contains(" IN (", StringComparison.OrdinalIgnoreCase));
            _output.WriteLine($"/api/Sales/SalesInvoices ({invArr.Count} rows): total={invList.Commands.Count}, prefetch={invListPrefetchQueries}");
            // Distinct prefixes on SalesInvoices: inv, cus, term, il, item, uom, tax, acct -> at most 8 batched IN queries (0 N+1)
            Assert.True(invList.Commands.Count <= 16, $"SalesInvoices with 100 rows executed {invList.Commands.Count} queries (possible N+1).");

            // Measure invoice detail
            var invDetail = await Send($"/api/Sales/SalesInvoice?id={sampleInvoicePublicId}");
            Assert.Equal(HttpStatusCode.OK, invDetail.Status);
            _output.WriteLine($"/api/Sales/SalesInvoice: total={invDetail.Commands.Count}");
            Assert.True(invDetail.Commands.Count <= 25, $"SalesInvoice detail executed {invDetail.Commands.Count} queries.");

            // Measure dashboard
            var dash = await Send("/api/Dashboard/Summary");
            Assert.Equal(HttpStatusCode.OK, dash.Status);
            var dashPrefetch = dash.Commands.Count(c => c.Contains("\"PublicId\"", StringComparison.Ordinal) && c.Contains("SELECT", StringComparison.OrdinalIgnoreCase) && c.Contains(" IN (", StringComparison.OrdinalIgnoreCase));
            _output.WriteLine($"/api/Dashboard/Summary: total={dash.Commands.Count}, prefetch={dashPrefetch}");
            Assert.True(dash.Commands.Count <= 42, $"Dashboard/Summary executed {dash.Commands.Count} queries.");
            Assert.True(dashPrefetch <= 8, $"Dashboard/Summary executed {dashPrefetch} PublicId prefetch queries (possible N+1).");

            // Measure GL report
            var gl = await Send("/api/Financials/GeneralLedger");
            Assert.Equal(HttpStatusCode.OK, gl.Status);
            _output.WriteLine($"/api/Financials/GeneralLedger: total={gl.Commands.Count}");
            Assert.True(gl.Commands.Count <= 15, $"GeneralLedger executed {gl.Commands.Count} queries.");

            // Measure bank register
            var bankReg = await Send($"/api/Banking/Accounts/{sampleBankPublicId}/Register");
            Assert.Equal(HttpStatusCode.OK, bankReg.Status);
            _output.WriteLine($"/api/Banking/Accounts/{{id}}/Register: total={bankReg.Commands.Count}");
            Assert.True(bankReg.Commands.Count <= 20, $"Bank Register executed {bankReg.Commands.Count} queries.");
        }
        finally
        {
            foreach (var file in new[] { sqlitePath, sqlitePath + "-wal", sqlitePath + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
