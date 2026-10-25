#nullable enable
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api.Service.Ai
{
    public interface IAnterAgentRunnerClient
    {
        bool IsConfigured { get; }

        Task<HttpResponseMessage> RunStreamAsync(
            string message,
            string userId,
            string? sessionId,
            IReadOnlyDictionary<string, string>? contextVariables,
            string? companyId,
            string? companySlug,
            CancellationToken cancellationToken,
            string? callerAssertion = null,
            string? accountGoUserId = null,
            string? companyPlan = null,
            string? activeModules = null,
            string? companyRole = null,
            string? subscriptionStatus = null);

        Task<HttpResponseMessage> ListSessionsAsync(string userId, CancellationToken cancellationToken);

        Task<HttpResponseMessage> GetSessionAsync(
            string sessionId,
            string userId,
            CancellationToken cancellationToken);

        Task<HttpResponseMessage> DeleteSessionAsync(
            string sessionId,
            string userId,
            CancellationToken cancellationToken);

        Task<HttpResponseMessage> GetExecutionStreamAsync(
            string executionId,
            int resumeFrom,
            string userId,
            CancellationToken cancellationToken);

        Task<HttpResponseMessage> CancelExecutionAsync(
            string executionId,
            string userId,
            CancellationToken cancellationToken);

        Task<HttpResponseMessage> ResumeExecutionAsync(
            string executionId,
            string userId,
            CancellationToken cancellationToken,
            string? callerAssertion = null);

        Task<HttpResponseMessage> ResolveApprovalAsync(
            string executionId,
            string toolCallId,
            string decision,
            string? reason,
            string userId,
            CancellationToken cancellationToken);
    }
}
