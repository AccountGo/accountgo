#nullable enable
using System;

namespace Api.Service.Ai
{
    /// <summary>
    /// Builds Anter external agent-runner URLs. Kept pure for unit testing.
    /// </summary>
    public static class AnterAgentRunnerPaths
    {
        public static string AgentBase(string baseUrl, string slug)
        {
            var root = (baseUrl ?? string.Empty).TrimEnd('/');
            var encodedSlug = Uri.EscapeDataString(slug ?? string.Empty);
            return $"{root}/api/v1/external/agent-runner/agents/by-slug/{encodedSlug}";
        }

        public static string RunStream(string baseUrl, string slug) =>
            $"{AgentBase(baseUrl, slug)}/run-stream";

        public static string Sessions(string baseUrl, string slug, string userId) =>
            $"{AgentBase(baseUrl, slug)}/sessions?userId={Uri.EscapeDataString(userId)}";

        public static string Session(string baseUrl, string slug, string sessionId, string userId) =>
            $"{AgentBase(baseUrl, slug)}/sessions/{Uri.EscapeDataString(sessionId)}?userId={Uri.EscapeDataString(userId)}";

        public static string ExecutionStream(string baseUrl, string executionId, int resumeFrom, string? userId)
        {
            var root = (baseUrl ?? string.Empty).TrimEnd('/');
            var path =
                $"{root}/api/v1/external/agent-runner/executions/{Uri.EscapeDataString(executionId)}/stream?resumeFrom={resumeFrom}";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                path += $"&userId={Uri.EscapeDataString(userId)}";
            }
            return path;
        }

        public static string ExecutionCancel(string baseUrl, string executionId, string? userId)
        {
            var root = (baseUrl ?? string.Empty).TrimEnd('/');
            var path =
                $"{root}/api/v1/external/agent-runner/executions/{Uri.EscapeDataString(executionId)}/cancel";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                path += $"?userId={Uri.EscapeDataString(userId)}";
            }
            return path;
        }

        public static string ExecutionResume(string baseUrl, string executionId, string? userId)
        {
            var root = (baseUrl ?? string.Empty).TrimEnd('/');
            var path =
                $"{root}/api/v1/external/agent-runner/executions/{Uri.EscapeDataString(executionId)}/resume";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                path += $"?userId={Uri.EscapeDataString(userId)}";
            }
            return path;
        }

        public static string Approval(
            string baseUrl,
            string executionId,
            string toolCallId,
            string? userId)
        {
            var root = (baseUrl ?? string.Empty).TrimEnd('/');
            var path =
                $"{root}/api/v1/external/agent-runner/executions/{Uri.EscapeDataString(executionId)}/approvals/{Uri.EscapeDataString(toolCallId)}";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                path += $"?userId={Uri.EscapeDataString(userId)}";
            }
            return path;
        }
    }
}
