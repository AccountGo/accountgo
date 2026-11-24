// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Options;
using Api.Service.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures.Mapping;

/// <summary>
/// Reads a scanned document's already-masked text lines through a dedicated, tool-free Anter agent and returns its suggested fields.
/// It has its own short-timeout HTTP client (not the 10-minute chat client), a fresh Anter session per call that is always deleted
/// afterwards, and it logs metadata only. Whatever it returns is still untrusted: <see cref="CaptureMappingGuards"/> runs after it.
/// </summary>
public sealed class AnterCaptureFieldMapper(
    HttpClient httpClient,
    IOptions<AnterOptions> anter,
    IOptions<DocumentCaptureOptions> options,
    ILogger<AnterCaptureFieldMapper> logger) : IVersionedCaptureFieldMapper
{
    public const string HttpClientName = "capture-field-mapper";
    private static readonly TimeSpan SessionDeleteTimeout = TimeSpan.FromSeconds(3);

    public string Name => "anter-capture-mapper";
    public string Version => $"{options.Value.AiMapping.AgentSlug}@{CaptureFieldMapping.ContractVersion}";

    public async Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken)
    {
        var settings = anter.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Anter is not configured.");

        var slug = options.Value.AiMapping.AgentSlug;
        var sessionId = Guid.NewGuid().ToString("N");
        // A synthetic user per workspace, never a person: no names or emails reach Anter.
        var userId = $"capture-mapper_{CaptureMappingScope.CompanyPublicId ?? "unknown"}";
        var captureId = CaptureMappingScope.CapturePublicId ?? "unknown";
        var started = Stopwatch.GetTimestamp();
        var outcome = "error";
        var status = 0;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.AiMapping.TimeoutSeconds));
        try
        {
            var body = AnterRunStreamBody.Build(BuildMessage(request), userId, sessionId, null);
            using var message = new HttpRequestMessage(HttpMethod.Post, AnterAgentRunnerPaths.RunStream(settings.BaseUrl, slug))
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            ApplyAuth(message, settings, acceptEventStream: true);

            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            response.EnsureSuccessStatusCode();
            var reply = await ReadReplyAsync(response, deadline.Token).ConfigureAwait(false);
            var fields = ParseFields(reply);
            outcome = "ok";
            return new CaptureMappingResult(fields, Name, Version, Array.Empty<string>());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = "timeout";
            throw new TimeoutException("The field mapper did not answer in time.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = ex is FormatException or JsonException ? "unusable" : "error";
            throw;
        }
        finally
        {
            await DeleteSessionAsync(settings, slug, sessionId, userId).ConfigureAwait(false);
            // Metadata only: never the payload, the reply or an exception message, which could carry document text.
            logger.LogInformation("Capture field mapper finished. CaptureId={CaptureId} Outcome={Outcome} HttpStatus={HttpStatus} LatencyMs={LatencyMs}",
                captureId, outcome, status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>The one message sent: the kind, the rules' fields and the masked lines. No images, no workspace data.</summary>
    internal static string BuildMessage(CaptureMappingRequest request) =>
        new JsonObject
        {
            ["kind"] = request.Kind,
            ["schemaVersion"] = request.SchemaVersion,
            ["rulesVersion"] = request.RulesVersion,
            ["ruleFields"] = JsonNode.Parse(request.RuleFieldsJson),
            ["lines"] = new JsonArray(request.Lines.Select(line => (JsonNode?)JsonValue.Create(line.Text)).ToArray()),
        }.ToJsonString();

    // The run-stream control events carry no assistant text. This list and the text and terminal rules below mirror what the web proxy
    // already does with the same stream (src/Web/src/assistant/reidentify.ts: RUNNER_CONTROL_EVENTS, detectContentTarget, isTerminalSseEvent).
    private static readonly HashSet<string> ControlEvents = new(StringComparer.Ordinal)
    {
        "started", "step", "artifact", "context_resolved", "tool_call", "tool_result", "tool_approval", "approval_required",
        "thinking", "reasoning", "done", "complete", "completed", "cancelled", "canceled", "error",
    };

    /// <summary>
    /// Reads server-sent events and returns the assistant's reply: the text of every non-control event joined in order. The event name is
    /// the <c>event:</c> line, else <c>event</c>, else <c>type</c> in the data; the text is the data's <c>content</c>, else
    /// <c>payload.text</c>, else <c>payload.content</c>. It returns as soon as a <c>done</c> or <c>complete</c> event (or
    /// <c>isComplete</c>) arrives, without waiting for the connection to close, and fails on an error or cancelled event.
    /// Events may arrive split across any number of network chunks.
    /// </summary>
    internal static async Task<string> ReadReplyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var reply = new StringBuilder();
        string? eventName = null;
        var data = new StringBuilder();

        // Returns true when the stream is finished.
        bool Dispatch()
        {
            var name = eventName;
            eventName = null;
            if (data.Length == 0)
                return false;
            var raw = data.ToString();
            data.Clear();
            if (raw == "[DONE]")
                return true;
            JsonObject? node;
            try
            {
                node = JsonNode.Parse(raw) as JsonObject;
            }
            catch (JsonException)
            {
                return false;
            }
            if (node is null)
                return false;

            var outer = (!string.IsNullOrEmpty(name) ? name : StringValue(node["event"]) ?? StringValue(node["type"]) ?? string.Empty).ToLowerInvariant();
            if (outer is "error" or "cancelled" or "canceled")
                throw new InvalidOperationException("The mapper agent did not finish.");
            if (outer is "done" or "complete" || node["isComplete"] is JsonValue complete && complete.TryGetValue<bool>(out var isComplete) && isComplete)
                return true;
            if (outer.Length > 0 && ControlEvents.Contains(outer))
                return false;

            var text = StringValue(node["content"]);
            if (text is null && node["payload"] is JsonObject payload)
                text = StringValue(payload["text"]) ?? StringValue(payload["content"]);
            if (text is not null)
                reply.Append(text);
            return false;
        }

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (line.Length == 0)
            {
                if (Dispatch())
                    return reply.ToString();
                continue;
            }
            if (line.StartsWith("event:", StringComparison.Ordinal))
                eventName = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart());
            }
        }
        Dispatch();
        return reply.ToString();
    }

    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The reply must be one JSON object (a markdown code fence around it is tolerated). Anything else is unusable.</summary>
    internal static string ParseFields(string reply)
    {
        var text = reply.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak > 0 && lastFence > firstBreak)
                text = text[(firstBreak + 1)..lastFence].Trim();
        }
        if (text.Length == 0 || text.Length > DocumentCaptureFieldSchemas.MaxProviderJsonLength)
            throw new FormatException("The mapper reply is empty or too long.");
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new FormatException("The mapper reply is not JSON.");
        }
        if (node is not JsonObject)
            throw new FormatException("The mapper reply is not a JSON object.");
        return node.ToJsonString();
    }

    private async Task DeleteSessionAsync(AnterOptions settings, string slug, string sessionId, string userId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(SessionDeleteTimeout);
            using var delete = new HttpRequestMessage(HttpMethod.Delete, AnterAgentRunnerPaths.Session(settings.BaseUrl, slug, sessionId, userId));
            ApplyAuth(delete, settings, acceptEventStream: false);
            using var response = await httpClient.SendAsync(delete, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Capture field mapper session cleanup failed. HttpStatus={HttpStatus}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Capture field mapper session cleanup failed. FailureType={FailureType}", ex.GetType().Name);
        }
    }

    private static void ApplyAuth(HttpRequestMessage request, AnterOptions settings, bool acceptEventStream)
    {
        request.Headers.TryAddWithoutValidation("x-api-key", settings.ApiKey);
        if (!string.IsNullOrWhiteSpace(settings.ProjectId))
            request.Headers.TryAddWithoutValidation("x-project-id", settings.ProjectId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptEventStream ? "text/event-stream" : "application/json"));
    }
}

/// <summary>Ambient identifiers for the mapping in progress, used only for the synthetic Anter user and metadata-only logs.</summary>
public static class CaptureMappingScope
{
    private static readonly AsyncLocal<(string? Company, string? Capture)> Current = new();

    public static string? CompanyPublicId => Current.Value.Company;
    public static string? CapturePublicId => Current.Value.Capture;

    public static IDisposable Begin(string companyPublicId, string capturePublicId)
    {
        var previous = Current.Value;
        Current.Value = (companyPublicId, capturePublicId);
        return new Restore(previous);
    }

    private sealed class Restore((string? Company, string? Capture) previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
