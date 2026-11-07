#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.Observability;

[Collection("WorkspaceSetupHttp")]
public sealed class RequestQueryBudgetTests
{
    private readonly ITestOutputHelper _output;
    public RequestQueryBudgetTests(ITestOutputHelper output) => _output = output;

    internal static readonly (string Path, int Budget)[] Budgets =
    [
        ("/api/auth/me", 9), ("/api/organizations", 16), ("/api/organizations/current", 16),
        ("/api/entitlements", 11), ("/api/security/capabilities", 5), ("/api/security/policy", 5),
        ("/api/linked-entries", 11), ("/api/Dashboard/Summary", 74), ("/api/Financials/Accounts", 39),
        ("/api/Banking/Accounts", 18), ("/api/Sales/SalesInvoices", 14), ("/api/Sales/Customers", 14),
        ("/api/Purchasing/Bills", 14), ("/api/Purchasing/Vendors", 15), ("/api/Financials/trialbalance", 24),
        ("/api/Financials/BalanceSheet", 32), ("/api/Financials/IncomeStatement", 21),
        ("/api/Financials/GeneralLedger", 25), ("/api/Financials/CashFlow", 38),
        ("/api/organizations/current/accounting-policy", 7), ("/api/organizations/current/operating-settings", 14),
        ("/api/Tax/Taxes", 13), ("/api/Common/PaymentTerms", 11), ("/api/Administration/financialyears", 8),
        ("/api/Banking/Reconciliations", 13),
    ];

    [Fact]
    public async Task WarmRequests_DoNotExceedTheirQueryBudgets()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"accountgo-query-budget-{Guid.NewGuid():N}.db");
        using var postgres = PostgresTestDatabase.CreateIfConfigured();
        using var collector = new RequestCommandCollector();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", postgres == null ? "Sqlite" : "Postgres");
            builder.UseSetting(postgres == null ? "ConnectionStrings:SqliteConnection" : "ConnectionStrings:PostgresConnection",
                postgres?.ConnectionString ?? $"Data Source={sqlitePath}");
            builder.UseSetting("SEED_DEMO_DATA", "false");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        });
        try
        {
            using var client = factory.CreateClient();
            string token;
            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var user = new ApplicationUser { UserName = "budget@example.test", Email = "budget@example.test", EmailConfirmed = true };
                Assert.True((await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded);
                var security = services.GetRequiredService<Services.Security.ISecurityService>();
                security.EnsureDefaultPermissionsAndRoles();
                security.AddUser(user.Email, user.Email, "Budget", "Owner", new List<int>());
                token = (await services.GetRequiredService<IAuthenticationService>().CreateToken(user, true)).AccessToken;
            }
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/organizations"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = JsonContent.Create(new { name = "Budget Co", slug = "budget-co", currency = "USD" });
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var member = db.CompanyMembers.IgnoreQueryFilters().Include(m => m.SecurityRoles).Single(m => m.Company.Slug == "budget-co");
                var assigned = member.SecurityRoles.Select(r => r.SecurityRoleId).ToHashSet();
                foreach (var roleId in db.SecurityRoles.Where(r => r.Name != "SystemAdministrators").Select(r => r.Id).ToList())
                    if (assigned.Add(roleId)) db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });
                await db.SaveChangesAsync();
            }
            async Task<(HttpStatusCode Status, IReadOnlyList<string> Commands)> Send(string path)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("X-AccountGo-Org", "budget-co");
                var commands = new List<string>();
                collector.Current = commands;
                try
                {
                    using var response = await client.SendAsync(request);
                    await response.Content.ReadAsStringAsync();
                    return (response.StatusCode, commands);
                }
                finally { collector.Current = null; }
            }
            foreach (var budget in Budgets) await Send(budget.Path);
            var failures = new List<string>();
            foreach (var budget in Budgets)
            {
                var (status, commands) = await Send(budget.Path);
                var expectedStatus = budget.Path is "/api/security/policy" or "/api/Banking/Reconciliations"
                    ? HttpStatusCode.NotFound : HttpStatusCode.OK;
                Assert.Equal(expectedStatus, status);
                Assert.NotEmpty(commands);
                _output.WriteLine($"{budget.Path}: {commands.Count} ({(int)status})");
                if (commands.Count > budget.Budget)
                {
                    var grouped = string.Join("\n\n", commands.GroupBy(x => x).Select(g => $"{g.Count()}x {g.Key}"));
                    failures.Add($"{budget.Path}: {commands.Count} commands, budget {budget.Budget}\n{grouped}");
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n\n", failures));
        }
        finally
        {
            foreach (var file in new[] { sqlitePath, sqlitePath + "-wal", sqlitePath + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    internal sealed class RequestCommandCollector : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable _all;
        private readonly List<IDisposable> _subscriptions = [];
        public List<string>? Current { get; set; }
        public RequestCommandCollector() => _all = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name) _subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuted.Name && value.Value is CommandExecutedEventData data && Current is { } commands)
                lock (commands) commands.Add(data.Command.CommandText);
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { _all.Dispose(); foreach (var subscription in _subscriptions) subscription.Dispose(); }
    }
}
