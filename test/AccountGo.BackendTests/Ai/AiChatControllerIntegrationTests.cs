#nullable disable
using System;
using System.Collections.Concurrent;
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
using Api.Options;
using Api.Service;
using Api.Service.Ai;
using Core.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.Ai
{
    public class AiChatControllerIntegrationTests : IAsyncLifetime
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "accountgo_aichat_" + Guid.NewGuid().ToString("N") + ".db");
        private readonly RecordingHttpMessageHandler _anterHandler = new();
        private WebApplicationFactory<Program> _factory;
        private HttpClient _client;
        private string _token;
        private string _userId;
        private string _slugA;
        private int _companyIdA;
        private string _slugB;
        private int _companyIdB;

        public async Task InitializeAsync()
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _path);
                builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                builder.UseSetting("Anter:ApiKey", "proj_api-key_test_secret");
                builder.UseSetting("Anter:ProjectId", "test-anter-project");
                builder.UseSetting("Anter:AgentSlug", "accountgo-assistant");
                builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                builder.UseSetting("PUBLIC_API_URL", "https://api.example");

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAnterAgentRunnerClient>();
                    services.AddScoped<IAnterAgentRunnerClient>(sp =>
                    {
                        var httpClient = new HttpClient(_anterHandler, disposeHandler: false);
                        var opts = sp.GetRequiredService<IOptions<AnterOptions>>();
                        return new AnterAgentRunnerClient(httpClient, opts);
                    });
                });
            });

            _client = _factory.CreateClient();

            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser
                {
                    UserName = "chat-tester@example.com",
                    Email = "chat-tester@example.com",
                    EmailConfirmed = true
                };
                var createResult = await userManager.CreateAsync(user);
                Assert.True(createResult.Succeeded);
                _userId = user.Id;

                var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
                var tokenDto = await authService.CreateToken(user, populateExp: true);
                _token = tokenDto.AccessToken;
            }

            _slugA = "chat-org-a-" + Guid.NewGuid().ToString("N")[..6];
            _companyIdA = await CreateOrgAsync(_slugA, "Chat Org A");

            _slugB = "chat-org-b-" + Guid.NewGuid().ToString("N")[..6];
            _companyIdB = await CreateOrgAsync(_slugB, "Chat Org B");
        }

        private async Task<int> CreateOrgAsync(string slug, string name)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizations");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Content = JsonContent.Create(new { name, slug });
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var idProp = doc.RootElement.GetProperty("id");
            return idProp.ValueKind == JsonValueKind.String
                ? int.Parse(idProp.GetString()!)
                : idProp.GetInt32();
        }

        public Task DisposeAsync()
        {
            _client?.Dispose();
            _factory?.Dispose();
            _anterHandler?.Dispose();
            foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            return Task.CompletedTask;
        }

        [Fact]
        public async Task RunStream_AttachesScopedUserAndSignedCallerAssertion_AndStreamsSse()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/run-stream");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Add("X-AccountGo-Org", _slugA);
            req.Content = JsonContent.Create(new
            {
                message = "What is our trial balance?",
                sessionId = "session_test_1",
                contextVariables = new
                {
                    pageRoute = $"/org/{_slugA}/reports",
                    pageTitle = "Reports",
                    companySlug = "forged-victim",
                    companyId = "999999",
                    userId = "forged-user",
                    companyPlan = "enterprise",
                    activeModules = "financials,reports,audit",
                    companyRole = "Owner",
                    subscriptionStatus = "active"
                }
            });

            var response = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var sseText = await response.Content.ReadAsStringAsync();
            Assert.Contains("event: started", sseText);

            Assert.True(_anterHandler.Requests.TryDequeue(out var captured));
            Assert.EndsWith("/api/v1/external/agent-runner/agents/by-slug/accountgo-assistant/run-stream", captured.Uri.ToString());

            using var bodyDoc = JsonDocument.Parse(captured.Body);
            var root = bodyDoc.RootElement;
            var cvs = root.GetProperty("contextVariables");
            Assert.Equal(AnterRunStreamBody.BuildScopedUserId(_companyIdA, _userId), cvs.GetProperty("userId").GetString());
            Assert.Equal(_userId, cvs.GetProperty("accountGoUserId").GetString());
            Assert.Equal(_companyIdA.ToString(), cvs.GetProperty("companyId").GetString());
            Assert.Equal(_companyIdA.ToString(), cvs.GetProperty("organizationId").GetString());
            Assert.Equal(_slugA, cvs.GetProperty("companySlug").GetString());
            Assert.Equal($"/org/{_slugA}/reports", cvs.GetProperty("pageRoute").GetString());
            Assert.False(string.IsNullOrWhiteSpace(cvs.GetProperty("companyPlan").GetString()));
            Assert.NotEqual("enterprise", cvs.GetProperty("companyPlan").GetString());
            Assert.False(string.IsNullOrWhiteSpace(cvs.GetProperty("activeModules").GetString()));
            Assert.NotEqual("financials,reports,audit", cvs.GetProperty("activeModules").GetString());
            Assert.False(string.IsNullOrWhiteSpace(cvs.GetProperty("companyRole").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(cvs.GetProperty("subscriptionStatus").GetString()));

            var assertion = root
                .GetProperty("mcpPassThroughHeaders")
                .GetProperty(McpCallerAssertionService.HeaderName)
                .GetString();
            Assert.NotNull(assertion);

            using var scope = _factory.Services.CreateScope();
            var assertionService = scope.ServiceProvider.GetRequiredService<IMcpCallerAssertionService>();
            Assert.True(assertionService.TryValidateAssertion(assertion, out var payload));
            Assert.Equal(_userId, payload.UserId);
            Assert.Equal(_companyIdA, payload.CompanyId);
            Assert.Equal(_slugA, payload.CompanySlug);
        }

        [Fact]
        public async Task ListSessions_PartitionsUpstreamUserIdByCompany()
        {
            while (_anterHandler.Requests.TryDequeue(out _)) { }

            using var reqA = new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/sessions");
            reqA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            reqA.Headers.Add("X-AccountGo-Org", _slugA);
            var resA = await _client.SendAsync(reqA);
            Assert.Equal(HttpStatusCode.OK, resA.StatusCode);

            using var reqB = new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/sessions");
            reqB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            reqB.Headers.Add("X-AccountGo-Org", _slugB);
            var resB = await _client.SendAsync(reqB);
            Assert.Equal(HttpStatusCode.OK, resB.StatusCode);

            Assert.True(_anterHandler.Requests.TryDequeue(out var capA));
            Assert.True(_anterHandler.Requests.TryDequeue(out var capB));

            Assert.Contains($"userId={Uri.EscapeDataString(AnterRunStreamBody.BuildScopedUserId(_companyIdA, _userId))}", capA.Uri.Query);
            Assert.Contains($"userId={Uri.EscapeDataString(AnterRunStreamBody.BuildScopedUserId(_companyIdB, _userId))}", capB.Uri.Query);
            Assert.NotEqual(capA.Uri.Query, capB.Uri.Query);
        }

        [Fact]
        public async Task LiveAnterRoundTrip_WhenApiKeyPresent_StreamsFromLiveAccountGoAssistant()
        {
            var liveKey = Environment.GetEnvironmentVariable("ANTER_PROJECT_API_KEY");
            var liveProject = Environment.GetEnvironmentVariable("ANTER_PROJECT_ID") ?? "test-anter-project";
            if (string.IsNullOrWhiteSpace(liveKey))
            {
                return;
            }

            await using var liveFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _path);
                builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                builder.UseSetting("Anter:BaseUrl", "https://agents.anter.ai");
                builder.UseSetting("Anter:ApiKey", liveKey);
                builder.UseSetting("Anter:ProjectId", liveProject);
                builder.UseSetting("Anter:AgentSlug", "accountgo-assistant");
            });

            using var liveClient = liveFactory.CreateClient();
            var sessionId = "session_live_e2e_" + Guid.NewGuid().ToString("N")[..10];

            try
            {
                using var runReq = new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/run-stream");
                runReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                runReq.Headers.Add("X-AccountGo-Org", _slugA);
                runReq.Content = JsonContent.Create(new
                {
                    message = "Reply in one short sentence naming the companySlug and pageTitle from contextVariables.",
                    sessionId,
                    contextVariables = new
                    {
                        pageRoute = $"/org/{_slugA}/financials",
                        pageTitle = "Financials"
                    }
                });

                var runRes = await liveClient.SendAsync(runReq);
                Assert.True(
                    runRes.StatusCode == HttpStatusCode.OK || runRes.StatusCode == HttpStatusCode.Created,
                    $"Expected 200/201 from live /api/ai/chat/run-stream but got {runRes.StatusCode}");

                var streamBody = await runRes.Content.ReadAsStringAsync();
                Assert.Contains("event: started", streamBody);
                Assert.Contains("\"type\":\"content\"", streamBody);

                using var listReq = new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/sessions");
                listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                listReq.Headers.Add("X-AccountGo-Org", _slugA);
                var listRes = await liveClient.SendAsync(listReq);
                Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
                var listBody = await listRes.Content.ReadAsStringAsync();
                Assert.Contains(sessionId, listBody);

                using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/chat/sessions/{Uri.EscapeDataString(sessionId)}");
                getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                getReq.Headers.Add("X-AccountGo-Org", _slugA);
                var getRes = await liveClient.SendAsync(getReq);
                Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
                var getBody = await getRes.Content.ReadAsStringAsync();
                Assert.Contains(_slugA, getBody, StringComparison.OrdinalIgnoreCase);

                // Verify Org B does not see Org A's live session
                using var listReqB = new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/sessions");
                listReqB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                listReqB.Headers.Add("X-AccountGo-Org", _slugB);
                var listResB = await liveClient.SendAsync(listReqB);
                Assert.Equal(HttpStatusCode.OK, listResB.StatusCode);
                var listBodyB = await listResB.Content.ReadAsStringAsync();
                Assert.DoesNotContain(sessionId, listBodyB);
            }
            finally
            {
                using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/ai/chat/sessions/{Uri.EscapeDataString(sessionId)}");
                delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                delReq.Headers.Add("X-AccountGo-Org", _slugA);
                await liveClient.SendAsync(delReq);
            }
        }

        private sealed record CapturedAnterRequest(Uri Uri, string Body);

        private sealed class RecordingHttpMessageHandler : HttpMessageHandler
        {
            public ConcurrentQueue<CapturedAnterRequest> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var body = request.Content != null
                    ? await request.Content.ReadAsStringAsync(cancellationToken)
                    : string.Empty;
                Requests.Enqueue(new CapturedAnterRequest(request.RequestUri, body));

                if (request.RequestUri.AbsolutePath.EndsWith("/run-stream", StringComparison.Ordinal))
                {
                    var sse = "event: started\ndata: {\"sessionId\":\"session_test_1\"}\n\ndata: {\"type\":\"content\",\"payload\":{\"text\":\"Balanced.\"}}\n\n";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
