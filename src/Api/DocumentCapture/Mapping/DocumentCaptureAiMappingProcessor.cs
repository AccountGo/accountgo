// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Data;
using Api.DocumentCaptures.Parsing;
using Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.DocumentCaptures.Mapping;

/// <summary>A per-instance safety cap on mapping calls per minute.</summary>
public sealed class CaptureAiMappingRateLimiter(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentQueue<DateTimeOffset> _starts = new();

    public bool TryAcquire(int perMinute)
    {
        var now = _clock.GetUtcNow();
        while (_starts.TryPeek(out var oldest) && now - oldest >= TimeSpan.FromMinutes(1))
            _starts.TryDequeue(out _);
        if (_starts.Count >= perMinute)
            return false;
        _starts.Enqueue(now);
        return true;
    }
}

/// <summary>
/// Claims one queued mapping job with a lease, maps the capture's masked text, and applies the result with a single conditional update
/// that only succeeds while the capture is still the ready, unaccepted extraction the job was queued for (rule AI-06). The cache
/// only ever looks inside the capture's own company (rule AI-07).
/// </summary>
public sealed class DocumentCaptureAiMappingProcessor(
    ApiDbContext db,
    ICaptureFieldMapper mapper,
    ICaptureAiMappingPolicy policy,
    CaptureAiMappingRateLimiter limiter,
    IOptions<DocumentCaptureOptions> options,
    ILogger<DocumentCaptureAiMappingProcessor> logger,
    ISecurityAuditService? audit = null,
    TimeProvider? timeProvider = null)
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <summary>Processes at most one job. Returns true when a job was claimed (so the caller can immediately look for another).</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.AiMapping;
        if (!settings.Enabled)
            return false;
        var now = _clock.GetUtcNow().UtcDateTime;

        // Jobs whose lease expired after the last allowed attempt are finished as failed.
        await db.DocumentCaptureAiMappings.IgnoreQueryFilters()
            .Where(job => job.Status == "running" && job.LeaseUntil < now && job.Attempts >= MaxAttempts)
            .ExecuteUpdateAsync(set => set.SetProperty(job => job.Status, "failed").SetProperty(job => job.Outcome, "attempts")
                .SetProperty(job => job.CompletedAt, now), cancellationToken).ConfigureAwait(false);

        var candidate = await db.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(job => job.Attempts < MaxAttempts && (job.Status == "queued" || (job.Status == "running" && job.LeaseUntil < now)))
            .OrderBy(job => job.Id)
            .Select(job => new { job.Id, job.CompanyId, job.CaptureId, job.ExtractionGeneration, job.Kind, job.ContentHash, job.RulesVersion })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (candidate is null || !limiter.TryAcquire(settings.MaxMappingsPerMinute))
            return false;

        var claimed = await db.DocumentCaptureAiMappings.IgnoreQueryFilters()
            .Where(job => job.Id == candidate.Id && job.Attempts < MaxAttempts
                && (job.Status == "queued" || (job.Status == "running" && job.LeaseUntil < now)))
            .ExecuteUpdateAsync(set => set.SetProperty(job => job.Status, "running")
                .SetProperty(job => job.Attempts, job => job.Attempts + 1)
                .SetProperty(job => job.LeaseUntil, now + Lease), cancellationToken).ConfigureAwait(false);
        if (claimed != 1)
            return true;

        var started = _clock.GetTimestamp();
        string outcome;
        string? mapperName = null;
        string? mapperVersion = null;
        try
        {
            (outcome, mapperName, mapperVersion) = await RunJobAsync(candidate.Id, candidate.CompanyId, candidate.CaptureId,
                candidate.ExtractionGeneration, candidate.Kind, candidate.ContentHash, candidate.RulesVersion, started, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Capture AI mapping job failed. JobId={JobId} FailureType={FailureType}", candidate.Id, ex.GetType().Name);
            outcome = "error";
            await FinishAsync(candidate.Id, "failed", outcome, mapperName, mapperVersion, started, null, CancellationToken.None).ConfigureAwait(false);
        }

        var latency = (int)_clock.GetElapsedTime(started).TotalMilliseconds;
        logger.LogInformation("Capture AI mapping job finished. JobId={JobId} Outcome={Outcome} LatencyMs={LatencyMs}", candidate.Id, outcome, latency);
        return true;
    }

    private async Task<(string Outcome, string? MapperName, string? MapperVersion)> RunJobAsync(
        int jobId, int companyId, int captureId, int generation, string kind, string contentHash, string rulesVersion,
        long started, CancellationToken cancellationToken)
    {
        var capture = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null))
            .Select(c => new
            {
                c.PublicId, c.Status, c.ExtractionGeneration, c.AcceptedAt, c.RejectedAt, c.FieldsWipedAt,
                c.FieldsJson, c.WarningsJson, c.RawText, c.SchemaVersion,
                CompanyPublicId = db.Companies.IgnoreQueryFilters().Where(company => company.Id == companyId).Select(company => company.PublicId).First()
            })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        // AI-06: only the ready, unaccepted, unchanged extraction the job was queued for may be improved.
        if (capture is null || capture.Status != "ready" || capture.ExtractionGeneration != generation
            || capture.AcceptedAt is not null || capture.RejectedAt is not null || capture.FieldsWipedAt is not null
            || string.IsNullOrEmpty(capture.FieldsJson))
        {
            await FinishAsync(jobId, "skipped", "not-ready", null, null, started, null, cancellationToken).ConfigureAwait(false);
            return ("skipped", null, null);
        }

        // Consent can be withdrawn between queueing and running.
        if (!await policy.IsAllowedAsync(companyId, kind, cancellationToken).ConfigureAwait(false))
        {
            await FinishAsync(jobId, "skipped", "policy", null, null, started, null, cancellationToken).ConfigureAwait(false);
            return ("skipped", null, null);
        }

        var (mapperName, mapperVersion) = mapper is IVersionedCaptureFieldMapper versioned
            ? (versioned.Name, versioned.Version)
            : (RuleOnlyFieldMapper.Name, RuleOnlyFieldMapper.Version);

        string finalFields;
        string? hintsJson;
        IReadOnlyList<string> warnings;
        var outcome = "applied";

        // AI-07: reuse a finished mapping of the same document, but only inside the same workspace.
        var cached = await db.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(job => job.CompanyId == companyId && job.Id != jobId && job.Status == "done" && job.ContentHash == contentHash
                && job.Kind == kind && job.RulesVersion == rulesVersion && job.MapperVersion == mapperVersion && job.MappedFieldsJson != null)
            .OrderByDescending(job => job.Id)
            .Select(job => new { job.MappedFieldsJson, job.RuleHintsJson })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            finalFields = cached.MappedFieldsJson!;
            hintsJson = cached.RuleHintsJson;
            warnings = Array.Empty<string>();
            outcome = "cached";
        }
        else
        {
            var lines = (capture.RawText ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Take(options.Value.AiMapping.MaxLines)
                .Select((text, index) => new TextLine(1, text, 0, index, 1, index + 1, TextLineSource.Ocr))
                .ToArray();
            CaptureMappingOutcome result;
            using (CaptureMappingScope.Begin(capture.CompanyPublicId, capture.PublicId))
            {
                result = await CaptureFieldMappingRunner.RunAsync(mapper, kind, capture.SchemaVersion, capture.FieldsJson, lines,
                    rulesVersion, cancellationToken, TimeSpan.FromSeconds(options.Value.AiMapping.TimeoutSeconds)).ConfigureAwait(false);
            }
            mapperName = result.MapperName;
            mapperVersion = result.MapperVersion;
            warnings = result.Warnings;
            if (warnings.Contains(CaptureMappingGuards.WarningUnavailable))
            {
                var attempts = await db.DocumentCaptureAiMappings.IgnoreQueryFilters().AsNoTracking()
                    .Where(job => job.Id == jobId).Select(job => job.Attempts).FirstAsync(cancellationToken).ConfigureAwait(false);
                // Retry later while attempts remain; otherwise the rule result simply stands.
                await FinishAsync(jobId, attempts < MaxAttempts ? "queued" : "failed", "unavailable", mapperName, mapperVersion, started, null,
                    cancellationToken, requeue: attempts < MaxAttempts).ConfigureAwait(false);
                return ("unavailable", mapperName, mapperVersion);
            }

            if (!TryCanonicalize(kind, capture.SchemaVersion, result.FieldsJson, out finalFields))
            {
                await FinishAsync(jobId, "failed", "invalid", mapperName, mapperVersion, started, null, cancellationToken).ConfigureAwait(false);
                return ("invalid", mapperName, mapperVersion);
            }
            hintsJson = result.RuleValueHints.Count == 0 ? null : BoundedHints(result.RuleValueHints);
        }

        var mergedWarnings = MergeWarnings(capture.WarningsJson, warnings);
        if (string.Equals(finalFields, capture.FieldsJson, StringComparison.Ordinal) && string.Equals(mergedWarnings, MergeWarnings(capture.WarningsJson, []), StringComparison.Ordinal))
        {
            await FinishAsync(jobId, "done", "unchanged", mapperName, mapperVersion, started, hintsJson, cancellationToken, mappedFields: finalFields).ConfigureAwait(false);
            await AuditAsync(companyId, capture.PublicId, mapperVersion, started, "unchanged").ConfigureAwait(false);
            return ("unchanged", mapperName, mapperVersion);
        }

        // AI-06: one conditional update. If the capture was accepted, regenerated, wiped or changed meanwhile, nothing is written.
        var originalFields = capture.FieldsJson;
        var updated = await db.DocumentCaptures.IgnoreQueryFilters()
            .Where(c => c.CompanyId == companyId && c.Id == captureId && c.Status == "ready" && c.ExtractionGeneration == generation
                && c.AcceptedAt == null && c.RejectedAt == null && c.FieldsWipedAt == null && c.FieldsJson == originalFields
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.FieldsJson, finalFields).SetProperty(c => c.WarningsJson, mergedWarnings), cancellationToken)
            .ConfigureAwait(false);
        if (updated != 1)
        {
            await FinishAsync(jobId, "skipped", "changed", mapperName, mapperVersion, started, null, cancellationToken).ConfigureAwait(false);
            return ("skipped", mapperName, mapperVersion);
        }

        await FinishAsync(jobId, "done", outcome, mapperName, mapperVersion, started, hintsJson, cancellationToken, mappedFields: finalFields).ConfigureAwait(false);
        await AuditAsync(companyId, capture.PublicId, mapperVersion, started, outcome).ConfigureAwait(false);
        return (outcome, mapperName, mapperVersion);
    }

    private static bool TryCanonicalize(string kind, int schemaVersion, string fieldsJson, out string canonical)
    {
        canonical = string.Empty;
        try
        {
            var sanitized = DocumentCaptureFieldSchemas.SanitizeProviderFields(kind, fieldsJson);
            sanitized = DocumentCaptureMasking.MaskFinancialFreeText(kind, sanitized);
            using var document = JsonDocument.Parse(sanitized);
            return DocumentCaptureFieldSchemas.TryValidate(kind, schemaVersion, document.RootElement, out canonical, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? BoundedHints(IReadOnlyDictionary<string, string> hints)
    {
        var json = JsonSerializer.Serialize(hints.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value));
        return json.Length <= 16_384 ? json : null;
    }

    private static string MergeWarnings(string? existingJson, IReadOnlyList<string> added)
    {
        var all = new List<string>();
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                if (JsonNode.Parse(existingJson) is JsonArray array)
                    all.AddRange(array.Select(item => item?.GetValue<string>()).Where(item => item is not null)!);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // A malformed stored list is replaced by the mapper's own warnings.
            }
        }
        all.AddRange(added);
        return JsonSerializer.Serialize(all.Select(DocumentCaptureWarningCodes.Sanitize).Distinct(StringComparer.Ordinal).Take(32));
    }

    private async Task FinishAsync(int jobId, string status, string outcome, string? mapperName, string? mapperVersion, long started,
        string? hintsJson, CancellationToken cancellationToken, bool requeue = false, string? mappedFields = null)
    {
        var latency = (int)_clock.GetElapsedTime(started).TotalMilliseconds;
        var now = _clock.GetUtcNow().UtcDateTime;
        await db.DocumentCaptureAiMappings.IgnoreQueryFilters().Where(job => job.Id == jobId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(job => job.Status, status)
                .SetProperty(job => job.Outcome, outcome)
                .SetProperty(job => job.MapperName, mapperName)
                .SetProperty(job => job.MapperVersion, mapperVersion)
                .SetProperty(job => job.LatencyMs, latency)
                .SetProperty(job => job.RuleHintsJson, hintsJson)
                .SetProperty(job => job.MappedFieldsJson, mappedFields)
                .SetProperty(job => job.LeaseUntil, (DateTime?)null)
                .SetProperty(job => job.CompletedAt, requeue ? (DateTime?)null : now), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AuditAsync(int companyId, string capturePublicId, string? mapperVersion, long started, string outcome)
    {
        if (audit is null)
            return;
        try
        {
            // Metadata only: no document text, no field values.
            await audit.LogEventAsync("document.capture.ai-mapped", "success", null, companyId,
                new { captureId = capturePublicId, mapperVersion, latencyMs = (int)_clock.GetElapsedTime(started).TotalMilliseconds, outcome }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Capture AI mapping audit failed. FailureType={FailureType}", ex.GetType().Name);
        }
    }
}
