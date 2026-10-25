#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Options;
using Microsoft.Extensions.Options;

namespace Api.Service.Ai
{
    /// <summary>
    /// HttpClient wrapper for Anter's external agent-runner API.
    /// </summary>
    public class AnterAgentRunnerClient(
        HttpClient httpClient,
        IOptions<AnterOptions> options) : IAnterAgentRunnerClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        private readonly AnterOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        public bool IsConfigured => _options.IsConfigured;

        public Task<HttpResponseMessage> RunStreamAsync(
            string message,
            string userId,
            string? sessionId,
            IReadOnlyDictionary<string, string>? contextVariables,
            string? companyId,
            string? companySlug,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var body = AnterRunStreamBody.Build(
                message,
                userId,
                sessionId,
                contextVariables,
                companyId,
                companySlug);
            var request = new HttpRequestMessage(HttpMethod.Post, AnterAgentRunnerPaths.RunStream(_options.BaseUrl, _options.AgentSlug))
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
            };
            ApplyAuth(request, acceptEventStream: true);
            return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        public Task<HttpResponseMessage> ListSessionsAsync(string userId, CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                AnterAgentRunnerPaths.Sessions(_options.BaseUrl, _options.AgentSlug, userId));
            ApplyAuth(request);
            return _httpClient.SendAsync(request, cancellationToken);
        }

        public Task<HttpResponseMessage> GetSessionAsync(
            string sessionId,
            string userId,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                AnterAgentRunnerPaths.Session(_options.BaseUrl, _options.AgentSlug, sessionId, userId));
            ApplyAuth(request);
            return _httpClient.SendAsync(request, cancellationToken);
        }

        public Task<HttpResponseMessage> DeleteSessionAsync(
            string sessionId,
            string userId,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var request = new HttpRequestMessage(
                HttpMethod.Delete,
                AnterAgentRunnerPaths.Session(_options.BaseUrl, _options.AgentSlug, sessionId, userId));
            ApplyAuth(request);
            return _httpClient.SendAsync(request, cancellationToken);
        }

        public Task<HttpResponseMessage> GetExecutionStreamAsync(
            string executionId,
            int resumeFrom,
            string userId,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                AnterAgentRunnerPaths.ExecutionStream(_options.BaseUrl, executionId, resumeFrom, userId));
            ApplyAuth(request, acceptEventStream: true);
            return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        public Task<HttpResponseMessage> CancelExecutionAsync(
            string executionId,
            string userId,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                AnterAgentRunnerPaths.ExecutionCancel(_options.BaseUrl, executionId, userId))
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            ApplyAuth(request);
            return _httpClient.SendAsync(request, cancellationToken);
        }

        public Task<HttpResponseMessage> ResumeExecutionAsync(
            string executionId,
            string userId,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                AnterAgentRunnerPaths.ExecutionResume(_options.BaseUrl, executionId, userId))
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            ApplyAuth(request, acceptEventStream: true);
            return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        public Task<HttpResponseMessage> ResolveApprovalAsync(
            string executionId,
            string toolCallId,
            string decision,
            string? reason,
            string userId,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var payload = new Dictionary<string, string?> { ["decision"] = decision };
            if (!string.IsNullOrWhiteSpace(reason))
            {
                payload["reason"] = reason;
            }

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                AnterAgentRunnerPaths.Approval(_options.BaseUrl, executionId, toolCallId, userId))
            {
                Content = JsonContent.Create(payload),
            };
            ApplyAuth(request);
            return _httpClient.SendAsync(request, cancellationToken);
        }

        private void EnsureConfigured()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Anter AI is not configured on this deployment.");
            }
        }

        private void ApplyAuth(HttpRequestMessage request, bool acceptEventStream = false)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
            if (!string.IsNullOrWhiteSpace(_options.ProjectId))
            {
                request.Headers.TryAddWithoutValidation("x-project-id", _options.ProjectId);
            }

            if (acceptEventStream)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            }
            else
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }
    }
}
