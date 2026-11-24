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

    /// <summary>
    /// Reads server-sent events and returns the assistant's reply: the text of a final event when there is one, otherwise the content
    /// events joined in order. Events may arrive split across any number of network chunks.
    /// </summary>
    internal static async Task<string> ReadReplyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = new StringBuilder();
        string? final = null;
        var data = new StringBuilder();

        void Dispatch()
        {
            if (data.Length == 0)
                return;
            var json = data.ToString();
            data.Clear();
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return;
            }
            var type = node?["type"]?.GetValue<string>();
            var text = node?["payload"]?["text"]?.GetValue<string>();
            switch (type)
            {
                case "content" when text is not null:
                    content.Append(text);
                    break;
                case "final" or "final_response" or "message_complete" when text is not null:
                    final = text;
                    break;
                case "error":
                    throw new InvalidOperationException("The mapper agent reported an error.");
            }
        }

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (line.Length == 0)
            {
                Dispatch();
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart());
            }
        }
        Dispatch();
        return final ?? content.ToString();
    }

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
