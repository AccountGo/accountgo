// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Globalization;
using System.Text.Json;
using Api.Data;
using Api.PublicIds;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Service;

public sealed record AcknowledgedCaptureDiscrepancy(
    string CapturePublicId,
    decimal SourceGrandTotal,
    decimal FormTotal,
    decimal Difference);

public sealed record BillCaptureReviewValidationResult(
    IActionResult? Failure,
    IReadOnlyList<AcknowledgedCaptureDiscrepancy> AcknowledgedDiscrepancies)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Read-only reconciliation policy consulted by existing bill writers (<c>SaveBill</c> and <c>ApproveBill</c>).
/// Independently loads canonical accepted capture fields, compares them against the persisted entity's
/// authoritative <see cref="PurchaseInvoiceHeader.Total"/>, and refuses unacknowledged or stale discrepancies
/// without ever trusting client-supplied total-match flags or OCR taxes.
/// </summary>
public static class BillCaptureReviewValidator
{
    public const int MaxCaptureReviews = 10;

    private static readonly HashSet<string> BillCaptureKinds = new(StringComparer.Ordinal)
    {
        "expense-receipt",
        "purchase-invoice",
    };

    public static IActionResult? ValidateForDraft(
        ApiDbContext db,
        ITenantContext tenantContext,
        int? billId,
        IReadOnlyList<Dto.Purchasing.CaptureReviewContext>? reviews)
    {
        if (reviews is null || reviews.Count == 0)
            return null;

        var structureError = ValidateReviewListStructure(reviews);
        if (structureError is not null)
            return structureError;

        var companyId = tenantContext.RequireCompanyId();
        foreach (var review in reviews)
        {
            var err = ValidateCaptureReference(db, companyId, review.CaptureId, billId, out _);
            if (err is not null)
                return err;
        }

        return null;
    }

    public static BillCaptureReviewValidationResult ValidateForApproval(
        ApiDbContext db,
        ITenantContext tenantContext,
        PurchaseInvoiceHeader bill,
        IReadOnlyList<Dto.Purchasing.CaptureReviewContext>? reviews)
    {
        if (reviews is { Count: > 0 })
        {
            var structureError = ValidateReviewListStructure(reviews);
            if (structureError is not null)
                return new(structureError, Array.Empty<AcknowledgedCaptureDiscrepancy>());
        }

        var companyId = tenantContext.RequireCompanyId();
        var roundedFormTotal = Round2(bill.Total());
        var reviewsByCaptureId = reviews is { Count: > 0 }
            ? reviews.ToDictionary(r => r.CaptureId)
            : new Dictionary<int, Dto.Purchasing.CaptureReviewContext>();

        var capturesToCheck = new List<DocumentCapture>();
        var seenCaptureIds = new HashSet<int>();

        if (reviews is { Count: > 0 })
        {
            foreach (var review in reviews)
            {
                var err = ValidateCaptureReference(db, companyId, review.CaptureId, bill.Id > 0 ? bill.Id : null, out var capture);
                if (err is not null)
                    return new(err, Array.Empty<AcknowledgedCaptureDiscrepancy>());
                if (seenCaptureIds.Add(capture!.Id))
                    capturesToCheck.Add(capture);
            }
        }

        if (bill.Id > 0)
        {
            var linkedCaptureIds = db.DocumentCaptureLinks.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.TargetType == "bill" && l.TargetId == bill.Id)
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
                Detail = "The bill total differs from one or more captured source document totals. Correct the bill or explicitly acknowledge each difference before approving.",
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

    private static IActionResult? ValidateReviewListStructure(IReadOnlyList<Dto.Purchasing.CaptureReviewContext> reviews)
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
        int captureId,
        int? billId,
        out DocumentCapture? capture)
    {
        var found = LoadLiveCapture(db, companyId, captureId);
        capture = found;
        if (found is null)
            return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });

        if (!BillCaptureKinds.Contains(found.Kind))
            return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                "This document capture kind cannot be reviewed for a bill.", found.PublicId);

        if (found.Status != "accepted" || string.IsNullOrWhiteSpace(found.AcceptedFieldsJson))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_STATE_CONFLICT",
                "Accept the captured fields before approving or attaching them to a bill.", found.PublicId);

        var existingLink = db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefault(l => l.CompanyId == companyId && l.CaptureId == found.Id);
        if (existingLink is not null && (existingLink.TargetType != "bill" || !billId.HasValue || existingLink.TargetId != billId.Value))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_LINK_CONFLICT",
                "This document capture is already linked to another record.", found.PublicId);

        return null;
    }

    private static DocumentCapture? LoadLiveCapture(ApiDbContext db, int companyId, int captureId) =>
        db.DocumentCaptures.AsNoTracking()
            .SingleOrDefault(c => c.CompanyId == companyId
                && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null));

    internal static decimal? ExtractCanonicalSourceGrandTotal(string? acceptedFieldsJson)
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
            if (!totals.TryGetProperty("grandTotal", out var grandTotal) || grandTotal.ValueKind != JsonValueKind.String)
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
