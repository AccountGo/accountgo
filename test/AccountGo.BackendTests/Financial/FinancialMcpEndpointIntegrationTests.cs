#nullable disable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public class FinancialMcpEndpointIntegrationTests : IAsyncLifetime
    {
        private const string TestAnterApiKey = "proj_api-key_test_anter_mcp_gate_secret";
        private readonly string _path = Path.Combine(Path.GetTempPath(), "accountgo_mcp_" + Guid.NewGuid().ToString("N") + ".db");
        private WebApplicationFactory<Program> _factory;
        private HttpClient _client;
        private string _token;
        private string _userId;
        private int _companyId;
        private string _slug;

        public async Task InitializeAsync()
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _path);
                builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                builder.UseSetting("Anter:ApiKey", TestAnterApiKey);
                builder.UseSetting("ANTER_API_KEY", TestAnterApiKey);
                builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                builder.UseSetting("PUBLIC_API_URL", "https://api.example");
            });

            _client = _factory.CreateClient();

            // Create user directly via UserManager and obtain JWT
            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser
                {
                    UserName = "mcp-tester@example.com",
                    Email = "mcp-tester@example.com",
                    EmailConfirmed = true
                };
                var createResult = await userManager.CreateAsync(user);
                Assert.True(createResult.Succeeded);
                _userId = user.Id;

                var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                var tokenDto = await authService.CreateToken(user, populateExp: true);
                _token = tokenDto.AccessToken;
            }

            // Create organization
            _slug = "mcp-org-" + Guid.NewGuid().ToString("N")[..8];
            using var createOrgReq = new HttpRequestMessage(HttpMethod.Post, "/api/organizations");
            createOrgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            createOrgReq.Content = JsonContent.Create(new { name = "MCP Test Org", slug = _slug });
            var createOrgRes = await _client.SendAsync(createOrgReq);
            Assert.Equal(HttpStatusCode.Created, createOrgRes.StatusCode);

            using var orgDoc = JsonDocument.Parse(await createOrgRes.Content.ReadAsStringAsync());
            var idProp = orgDoc.RootElement.GetProperty("id");
            _companyId = idProp.ValueKind == JsonValueKind.String
                ? int.Parse(idProp.GetString()!)
                : idProp.GetInt32();
        }

        public Task DisposeAsync()
        {
            _client?.Dispose();
            _factory?.Dispose();
            foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            return Task.CompletedTask;
        }

        [Fact]
        public async Task McpEndpoint_WithoutAuthentication_ReturnsUnauthorized()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json");

            var response = await _client.SendAsync(req);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task McpEndpoint_WithAuthenticationAndTenant_RespondsToToolsList_WithAllPhase1And2Tools()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 200/202 but got {response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("get_chart_of_accounts", body);
            Assert.Contains("get_trial_balance", body);
            Assert.Contains("get_balance_sheet", body);
            Assert.Contains("get_income_statement", body);
            Assert.Contains("get_general_ledger_transactions", body);
        }

        [Fact]
        public async Task McpEndpoint_WithUnauthorizedOrg_ReturnsNotFound()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", "unauthorized-org-" + Guid.NewGuid().ToString("N")[..6]);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            // TenantResolutionMiddleware returns 404 when user is not a member of the requested org
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task McpEndpoint_ToolsCall_ReturnsChartOfAccounts()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"get_chart_of_accounts\",\"arguments\":{}}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 200/202 but got {response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(_slug, body);
            Assert.Contains("totalAccounts", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task McpEndpoint_ToolsCall_ReturnsTrialBalance()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"get_trial_balance\",\"arguments\":{\"accountingBasis\":\"Accrual\"}}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 200/202 but got {response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(_slug, body);
            Assert.Contains("totalDebit", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task McpEndpoint_ToolsCall_ReturnsBalanceSheet()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"get_balance_sheet\",\"arguments\":{}}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 200/202 but got {response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(_slug, body);
            Assert.Contains("totalAssets", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task McpEndpoint_ToolsCall_ReturnsIncomeStatement()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"get_income_statement\",\"arguments\":{}}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 200/202 but got {response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(_slug, body);
            Assert.Contains("netIncome", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task McpEndpoint_ToolsCall_ReturnsGeneralLedgerTransactions()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slug);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"get_general_ledger_transactions\",\"arguments\":{\"take\":20}}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 200/202 but got {response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(_slug, body);
            Assert.Contains("totalCount", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task McpEndpoint_WithMcpGateAndValidCallerAssertion_ExecutesToolForAssertedTenant()
        {
            using var scope = _factory.Services.CreateScope();
            var assertionService = scope.ServiceProvider.GetRequiredService<Api.Service.Ai.IMcpCallerAssertionService>();
            var assertion = assertionService.MintAssertion(_userId, _companyId, _slug);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestAnterApiKey);
            req.Headers.Add(Api.Service.Ai.McpCallerAssertionService.HeaderName, assertion);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"get_chart_of_accounts\",\"arguments\":{}}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 200/202 but got {response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(_slug, body);
            Assert.Contains("totalAccounts", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task McpEndpoint_WithMcpGateAndTamperedCallerAssertion_ReturnsUnauthorized()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestAnterApiKey);
            req.Headers.Add(Api.Service.Ai.McpCallerAssertionService.HeaderName, "v1.eyJ1aWQiOiJmb3JnZWQifQ.forgedmac");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\",\"params\":{\"name\":\"get_chart_of_accounts\",\"arguments\":{}}}",
                Encoding.UTF8,
                "application/json");

            var response = await _client.SendAsync(req);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
