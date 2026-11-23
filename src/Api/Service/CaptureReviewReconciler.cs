// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text.Json;
using Api.Data;
using Api.DocumentCaptures;
using Api.PublicIds;
using Core.Domain;
using Core.Tenancy;
using Dto.DocumentCapture;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Service;

public sealed record AcknowledgedCaptureDiscrepancy(
    string CapturePublicId,
    decimal SourceGrandTotal,
    decimal FormTotal,
    decimal Difference);

public sealed record CaptureReviewValidationResult(
    IActionResult? Failure,
    IReadOnlyList<AcknowledgedCaptureDiscrepancy> AcknowledgedDiscrepancies)
{
    public bool Succeeded => Failure is null;
}

public sealed record CaptureReviewTargetPolicy(
    IReadOnlySet<string> AllowedKinds,
    string TargetType,
    string InvalidKindDetail,
    string StateConflictDetail,
    string DiscrepancyDetail,
    bool RejectTaxOnlyDifference = false);

/// <summary>
/// Shared read-only reconciliation and audit helper consulted by existing financial document writers
/// (bills and sales invoices). Independently loads canonical accepted capture fields, compares them against
/// the caller-supplied authoritative persisted entity total, and refuses unacknowledged or stale discrepancies
/// without trusting client-supplied total-match flags or OCR taxes.
/// </summary>
public static class CaptureReviewReconciler
{
    public const int MaxCaptureReviews = 10;

    public static IActionResult? ValidateForDraft(
        ApiDbContext db,
        ITenantContext tenantContext,
        CaptureReviewTargetPolicy policy,
        int? targetId,
        IReadOnlyList<CaptureReviewContext>? reviews)
    {
        if (reviews is null || reviews.Count == 0)
            return null;

        var structureError = ValidateReviewListStructure(reviews);
        if (structureError is not null)
            return structureError;

        var companyId = tenantContext.RequireCompanyId();
        foreach (var review in reviews)
        {
            var err = ValidateCaptureReference(db, companyId, policy, review.CaptureId, targetId, out _);
            if (err is not null)
                return err;
        }

        return null;
    }

    public static CaptureReviewValidationResult ValidateForCommit(
        ApiDbContext db,
        ITenantContext tenantContext,
        CaptureReviewTargetPolicy policy,
        int? targetId,
        decimal authoritativeTotal,
        IReadOnlyList<CaptureReviewContext>? reviews)
    {
        if (reviews is { Count: > 0 })
        {
            var structureError = ValidateReviewListStructure(reviews);
            if (structureError is not null)
                return new(structureError, Array.Empty<AcknowledgedCaptureDiscrepancy>());
        }

        var companyId = tenantContext.RequireCompanyId();
        var roundedFormTotal = Round2(authoritativeTotal);
        var reviewsByCaptureId = reviews is { Count: > 0 }
            ? reviews.ToDictionary(r => r.CaptureId)
            : new Dictionary<int, CaptureReviewContext>();

        var capturesToCheck = new List<DocumentCapture>();
        var seenCaptureIds = new HashSet<int>();

        if (reviews is { Count: > 0 })
        {
            foreach (var review in reviews)
            {
                var err = ValidateCaptureReference(db, companyId, policy, review.CaptureId, targetId, out var capture);
                if (err is not null)
                    return new(err, Array.Empty<AcknowledgedCaptureDiscrepancy>());
                if (seenCaptureIds.Add(capture!.Id))
                    capturesToCheck.Add(capture);
            }
        }

        if (targetId is > 0)
        {
            var linkedCaptureIds = db.DocumentCaptureLinks.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.TargetType == policy.TargetType && l.TargetId == targetId.Value)
                .OrderBy(l => l.LinkedAt)
                .Select(l => l.CaptureId)
                .ToList();

            if (linkedCaptureIds.Count > 0)
            {
                var linkedCaptures = db.DocumentCaptures.AsNoTracking()
                    .Where(c => c.CompanyId == companyId
                        && linkedCaptureIds.Contains(c.Id)
                        && c.Status == "accepted"
                        && c.AcceptedFieldsJson != null)
                    .ToList();

                var byId = linkedCaptures.ToDictionary(c => c.Id);
                foreach (var linkedId in linkedCaptureIds)
                {
                    if (byId.TryGetValue(linkedId, out var capture) && seenCaptureIds.Add(capture.Id))
                        capturesToCheck.Add(capture);
                }
            }
        }

        var unacknowledged = new List<object>();
        AcknowledgedCaptureDiscrepancy? firstMissing = null;
        var acknowledged = new List<AcknowledgedCaptureDiscrepancy>();

        foreach (var capture in capturesToCheck)
        {
            var sourceGrandTotal = ExtractCanonicalSourceGrandTotal(capture.AcceptedFieldsJson);
            if (!sourceGrandTotal.HasValue)
                continue;

            var roundedSourceTotal = Round2(sourceGrandTotal.Value);
            if (roundedSourceTotal == roundedFormTotal)
                continue;

            var difference = Round2(roundedFormTotal - roundedSourceTotal);
            reviewsByCaptureId.TryGetValue(capture.Id, out var review);
            var acknowledgedForCurrentValues = review is not null
                && review.DiscrepancyAcknowledged
                && review.SourceGrandTotal.HasValue
                && Round2(review.SourceGrandTotal.Value) == roundedSourceTotal
                && review.FormTotal.HasValue
                && Round2(review.FormTotal.Value) == roundedFormTotal;

            // A tax the document charges but the target doesn't carry can't be acknowledged away: it needs a tax rate or the tax in the costs.
            if (acknowledgedForCurrentValues
                && policy.RejectTaxOnlyDifference
                && ExtractCanonicalSourceTaxTotal(capture.AcceptedFieldsJson) is { } sourceTax
                && sourceTax > 0
                && Math.Abs(-difference - sourceTax) <= 0.01m)
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "CAPTURE_TAX_NOT_APPLIED",
                    Detail = $"Tax of {sourceTax:0.00} on the document isn't on this bill. Choose a tax rate or include it in the line costs.",
                };
                problem.Extensions["code"] = "CAPTURE_TAX_NOT_APPLIED";
                problem.Extensions["captureId"] = capture.PublicId;
                problem.Extensions["taxTotal"] = Round2(sourceTax);
                return new(new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest }, Array.Empty<AcknowledgedCaptureDiscrepancy>());
            }

            if (acknowledgedForCurrentValues)
            {
                acknowledged.Add(new AcknowledgedCaptureDiscrepancy(
                    capture.PublicId, roundedSourceTotal, roundedFormTotal, difference));
            }
            else
            {
                firstMissing ??= new AcknowledgedCaptureDiscrepancy(
                    capture.PublicId, roundedSourceTotal, roundedFormTotal, difference);
                unacknowledged.Add(new
                {
                    captureId = capture.PublicId,
                    sourceGrandTotal = roundedSourceTotal,
                    formTotal = roundedFormTotal,
                    difference,
                });
            }
        }

        if (firstMissing is not null)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "DOCUMENT_REVIEW_REQUIRED",
                Detail = policy.DiscrepancyDetail,
            };
            problem.Extensions["code"] = "DOCUMENT_REVIEW_REQUIRED";
            problem.Extensions["captureId"] = firstMissing.CapturePublicId;
            problem.Extensions["sourceGrandTotal"] = firstMissing.SourceGrandTotal;
            problem.Extensions["formTotal"] = firstMissing.FormTotal;
            problem.Extensions["difference"] = firstMissing.Difference;
            problem.Extensions["discrepancies"] = unacknowledged;
            return new(new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict }, Array.Empty<AcknowledgedCaptureDiscrepancy>());
        }

        return new(null, acknowledged);
    }

    public static void RecordDiscrepancyAcknowledgements(
        ISecurityAuditService? audit,
        ILogger logger,
        ITenantContext tenantContext,
        string? fallbackActor,
        string eventType,
        string targetLabel,
        string targetIdFieldName,
        string targetPublicId,
        IReadOnlyList<AcknowledgedCaptureDiscrepancy> acknowledgements)
    {
        if (audit is null || acknowledgements.Count == 0)
            return;

        try
        {
            var actor = tenantContext.UserId ?? fallbackActor;
            var companyId = tenantContext.RequireCompanyId();
            foreach (var ack in acknowledgements)
            {
                var details = new Dictionary<string, object?>
                {
                    [targetIdFieldName] = targetPublicId,
                    ["captureId"] = ack.CapturePublicId,
                    ["sourceGrandTotal"] = ack.SourceGrandTotal,
                    ["formTotal"] = ack.FormTotal,
                    ["difference"] = ack.Difference,
                    ["actor"] = actor,
                };
                audit.LogEventAsync(
                    eventType,
                    "success",
                    actor,
                    companyId,
                    details).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            // Best effort: the document commit already succeeded, so an audit failure must never turn it into a 500.
            logger.LogWarning(
                ex,
                "Failed to record capture discrepancy acknowledgement audit events for {TargetLabel} {TargetPublicId} (count={Count}).",
                targetLabel,
                targetPublicId,
                acknowledgements.Count);
        }
    }

    private static IActionResult? ValidateReviewListStructure(IReadOnlyList<CaptureReviewContext> reviews)
    {
        if (reviews.Count > MaxCaptureReviews)
            return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                $"At most {MaxCaptureReviews} capture review entries may be submitted.");

        var distinct = new HashSet<int>();
        foreach (var review in reviews)
        {
            if (review is null || review.CaptureId <= 0)
                return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                    "Each capture review entry must reference a valid capture identifier.");
            if (!distinct.Add(review.CaptureId))
                return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                    "Duplicate capture identifiers are not allowed in capture reviews.");
        }

        return null;
    }

    private static IActionResult? ValidateCaptureReference(
        ApiDbContext db,
        int companyId,
        CaptureReviewTargetPolicy policy,
        int captureId,
        int? targetId,
        out DocumentCapture? capture)
    {
        var found = LoadLiveCapture(db, companyId, captureId);
        capture = found;
        if (found is null)
            return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });

        if (!policy.AllowedKinds.Contains(found.Kind))
            return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                policy.InvalidKindDetail, found.PublicId);

        if (found.Status != "accepted" || string.IsNullOrWhiteSpace(found.AcceptedFieldsJson))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_STATE_CONFLICT",
                policy.StateConflictDetail, found.PublicId);

        var existingLink = db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefault(l => l.CompanyId == companyId && l.CaptureId == found.Id);
        if (existingLink is not null && (existingLink.TargetType != policy.TargetType || !targetId.HasValue || existingLink.TargetId != targetId.Value))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_LINK_CONFLICT",
                "This document capture is already linked to another record.", found.PublicId);

        return null;
    }

    private static DocumentCapture? LoadLiveCapture(ApiDbContext db, int companyId, int captureId) =>
        DocumentCaptureService.LiveCompanyCaptures(db, companyId).SingleOrDefault(c => c.Id == captureId);

    internal static decimal? ExtractCanonicalSourceGrandTotal(string? acceptedFieldsJson) =>
        ExtractCanonicalSourceTotal(acceptedFieldsJson, "grandTotal");

    internal static decimal? ExtractCanonicalSourceTaxTotal(string? acceptedFieldsJson) =>
        ExtractCanonicalSourceTotal(acceptedFieldsJson, "taxTotal");

    private static decimal? ExtractCanonicalSourceTotal(string? acceptedFieldsJson, string totalName)
    {
        if (string.IsNullOrWhiteSpace(acceptedFieldsJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(acceptedFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("sourceTotals", out var totals) || totals.ValueKind != JsonValueKind.Object)
                return null;
            if (!totals.TryGetProperty(totalName, out var grandTotal) || grandTotal.ValueKind != JsonValueKind.String)
                return null;
            var raw = grandTotal.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static ObjectResult Problem(int statusCode, string code, string detail, string? captureId = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = code,
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        if (captureId is not null)
            problem.Extensions["captureId"] = captureId;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
