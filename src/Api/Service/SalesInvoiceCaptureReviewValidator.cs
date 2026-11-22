// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Api.PublicIds;
using Core.Domain;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Service;

/// <summary>
/// Read-only reconciliation policy consulted by existing sales invoice writers (<c>SaveSalesInvoice</c> and
/// <c>IssueSalesInvoice</c>). Independently loads canonical accepted <c>sales-invoice</c> capture fields,
/// compares them against the persisted entity's authoritative <see cref="SalesInvoiceHeader.GrandTotal"/>, and
/// refuses unacknowledged or stale discrepancies without trusting client-supplied total-match flags or OCR taxes.
/// </summary>
public static class SalesInvoiceCaptureReviewValidator
{
    public const int MaxCaptureReviews = BillCaptureReviewValidator.MaxCaptureReviews;
    private const string SalesCaptureKind = "sales-invoice";

    public static IActionResult? ValidateForDraft(
        ApiDbContext db,
        ITenantContext tenantContext,
        int? invoiceId,
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
            var err = ValidateCaptureReference(db, companyId, review.CaptureId, invoiceId, out _);
            if (err is not null)
                return err;
        }

        return null;
    }

    public static BillCaptureReviewValidationResult ValidateForIssue(
        ApiDbContext db,
        ITenantContext tenantContext,
        SalesInvoiceHeader invoice,
        IReadOnlyList<Dto.Purchasing.CaptureReviewContext>? reviews)
    {
        if (reviews is { Count: > 0 })
        {
            var structureError = ValidateReviewListStructure(reviews);
            if (structureError is not null)
                return new(structureError, Array.Empty<AcknowledgedCaptureDiscrepancy>());
        }

        var companyId = tenantContext.RequireCompanyId();
        var roundedFormTotal = Round2(invoice.GrandTotal());
        var reviewsByCaptureId = reviews is { Count: > 0 }
            ? reviews.ToDictionary(r => r.CaptureId)
            : new Dictionary<int, Dto.Purchasing.CaptureReviewContext>();

        var capturesToCheck = new List<DocumentCapture>();
        var seenCaptureIds = new HashSet<int>();

        if (reviews is { Count: > 0 })
        {
            foreach (var review in reviews)
            {
                var err = ValidateCaptureReference(db, companyId, review.CaptureId, invoice.Id > 0 ? invoice.Id : null, out var capture);
                if (err is not null)
                    return new(err, Array.Empty<AcknowledgedCaptureDiscrepancy>());
                if (seenCaptureIds.Add(capture!.Id))
                    capturesToCheck.Add(capture);
            }
        }

        if (invoice.Id > 0)
        {
            var linkedCaptureIds = db.DocumentCaptureLinks.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.TargetType == "invoice" && l.TargetId == invoice.Id)
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
            var sourceGrandTotal = BillCaptureReviewValidator.ExtractCanonicalSourceGrandTotal(capture.AcceptedFieldsJson);
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
                Detail = "The invoice total differs from one or more captured source document totals. Correct the invoice or explicitly acknowledge each difference before issuing.",
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
        int? invoiceId,
        out DocumentCapture? capture)
    {
        var found = LoadLiveCapture(db, companyId, captureId);
        capture = found;
        if (found is null)
            return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });

        if (!string.Equals(found.Kind, SalesCaptureKind, StringComparison.Ordinal))
            return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                "This document capture kind cannot be reviewed for a sales invoice.", found.PublicId);

        if (found.Status != "accepted" || string.IsNullOrWhiteSpace(found.AcceptedFieldsJson))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_STATE_CONFLICT",
                "Accept the captured fields before issuing or attaching them to a sales invoice.", found.PublicId);

        var existingLink = db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefault(l => l.CompanyId == companyId && l.CaptureId == found.Id);
        if (existingLink is not null && (existingLink.TargetType != "invoice" || !invoiceId.HasValue || existingLink.TargetId != invoiceId.Value))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_LINK_CONFLICT",
                "This document capture is already linked to another record.", found.PublicId);

        return null;
    }

    private static DocumentCapture? LoadLiveCapture(ApiDbContext db, int companyId, int captureId) =>
        db.DocumentCaptures.AsNoTracking()
            .SingleOrDefault(c => c.CompanyId == companyId
                && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null));

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
