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

/// <summary>
/// Read-only reconciliation policy consulted by existing bill writers (<c>SaveBill</c> and <c>ApproveBill</c>).
/// Independently loads canonical accepted capture fields, compares them against authoritative bill totals, and
/// refuses unacknowledged or stale discrepancies without ever trusting client-supplied total-match flags or OCR taxes.
/// </summary>
public static class BillCaptureReviewValidator
{
    private static readonly HashSet<string> BillCaptureKinds = new(StringComparer.Ordinal)
    {
        "expense-receipt",
        "purchase-invoice",
    };

    public static IActionResult? ValidateForSave(
        ApiDbContext db,
        ITenantContext tenantContext,
        Dto.Purchasing.Bill billDto,
        bool approving)
    {
        var companyId = tenantContext.RequireCompanyId();
        var review = billDto.CaptureReview;

        if (!approving)
        {
            if (review is { CaptureId: > 0 })
                return ValidateCaptureReferenceOnly(db, companyId, review.CaptureId, billDto.Id > 0 ? billDto.Id : null);
            return null;
        }

        var authoritativeTotal = ComputeAuthoritativeBillTotal(db, billDto.Lines);
        return ValidateForApprovalCore(db, companyId, billDto.Id > 0 ? billDto.Id : null, authoritativeTotal, review);
    }

    public static IActionResult? ValidateForApprove(
        ApiDbContext db,
        ITenantContext tenantContext,
        PurchaseInvoiceHeader bill,
        Dto.Purchasing.CaptureReviewContext? review)
    {
        var companyId = tenantContext.RequireCompanyId();
        var authoritativeTotal = bill.Total();
        return ValidateForApprovalCore(db, companyId, bill.Id, authoritativeTotal, review);
    }

    private static IActionResult? ValidateCaptureReferenceOnly(
        ApiDbContext db,
        int companyId,
        int captureId,
        int? billId)
    {
        var capture = LoadLiveCapture(db, companyId, captureId);
        if (capture is null)
            return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });

        if (!BillCaptureKinds.Contains(capture.Kind))
            return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                "This document capture kind cannot be reviewed for a bill.", capture.PublicId);

        if (capture.Status != "accepted" || string.IsNullOrWhiteSpace(capture.AcceptedFieldsJson))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_STATE_CONFLICT",
                "Accept the captured fields before attaching them to a bill.", capture.PublicId);

        var existingLink = db.DocumentCaptureLinks.AsNoTracking()
            .SingleOrDefault(l => l.CompanyId == companyId && l.CaptureId == capture.Id);
        if (existingLink is not null && (existingLink.TargetType != "bill" || (billId.HasValue && existingLink.TargetId != billId.Value)))
            return Problem(StatusCodes.Status409Conflict, "DOCUMENT_LINK_CONFLICT",
                "This document capture is already linked to another record.", capture.PublicId);

        return null;
    }

    private static IActionResult? ValidateForApprovalCore(
        ApiDbContext db,
        int companyId,
        int? billId,
        decimal authoritativeTotal,
        Dto.Purchasing.CaptureReviewContext? review)
    {
        var roundedFormTotal = Round2(authoritativeTotal);
        var checkedCaptureIds = new HashSet<int>();

        if (review is { CaptureId: > 0 })
        {
            var capture = LoadLiveCapture(db, companyId, review.CaptureId);
            if (capture is null)
                return new NotFoundObjectResult(new { message = PublicIdErrorCodes.NotFoundMessage, code = PublicIdErrorCodes.UnknownId });

            if (!BillCaptureKinds.Contains(capture.Kind))
                return Problem(StatusCodes.Status400BadRequest, "DOCUMENT_REVIEW_INVALID",
                    "This document capture kind cannot be reviewed for a bill.", capture.PublicId);

            if (capture.Status != "accepted" || string.IsNullOrWhiteSpace(capture.AcceptedFieldsJson))
                return Problem(StatusCodes.Status409Conflict, "DOCUMENT_STATE_CONFLICT",
                    "Accept the captured fields before approving with capture review context.", capture.PublicId);

            var existingLink = db.DocumentCaptureLinks.AsNoTracking()
                .SingleOrDefault(l => l.CompanyId == companyId && l.CaptureId == capture.Id);
            if (existingLink is not null && (existingLink.TargetType != "bill" || (billId.HasValue && existingLink.TargetId != billId.Value)))
                return Problem(StatusCodes.Status409Conflict, "DOCUMENT_LINK_CONFLICT",
                    "This document capture is already linked to another record.", capture.PublicId);

            checkedCaptureIds.Add(capture.Id);
            var failure = CheckReconciliation(capture, roundedFormTotal, review);
            if (failure is not null)
                return failure;
        }

        if (billId.HasValue)
        {
            var linkedCaptureIds = db.DocumentCaptureLinks.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.TargetType == "bill" && l.TargetId == billId.Value)
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

                foreach (var capture in linkedCaptures)
                {
                    if (checkedCaptureIds.Contains(capture.Id))
                        continue;

                    var matchingReview = review is not null && review.CaptureId == capture.Id ? review : null;
                    var failure = CheckReconciliation(capture, roundedFormTotal, matchingReview);
                    if (failure is not null)
                        return failure;
                }
            }
        }

        return null;
    }

    private static IActionResult? CheckReconciliation(
        DocumentCapture capture,
        decimal roundedFormTotal,
        Dto.Purchasing.CaptureReviewContext? review)
    {
        var sourceGrandTotal = ExtractCanonicalSourceGrandTotal(capture.AcceptedFieldsJson);
        if (!sourceGrandTotal.HasValue)
            return null;

        var roundedSourceTotal = Round2(sourceGrandTotal.Value);
        if (roundedSourceTotal == roundedFormTotal)
            return null;

        var acknowledgedForCurrentValues = review is not null
            && review.CaptureId == capture.Id
            && review.DiscrepancyAcknowledged
            && review.SourceGrandTotal.HasValue
            && Round2(review.SourceGrandTotal.Value) == roundedSourceTotal
            && review.FormTotal.HasValue
            && Round2(review.FormTotal.Value) == roundedFormTotal;

        if (acknowledgedForCurrentValues)
            return null;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "DOCUMENT_REVIEW_REQUIRED",
            Detail = "The bill total differs from the captured source document total. Correct the bill or explicitly acknowledge the difference before approving.",
        };
        problem.Extensions["code"] = "DOCUMENT_REVIEW_REQUIRED";
        problem.Extensions["captureId"] = capture.PublicId;
        problem.Extensions["sourceGrandTotal"] = roundedSourceTotal;
        problem.Extensions["formTotal"] = roundedFormTotal;
        problem.Extensions["difference"] = Round2(roundedFormTotal - roundedSourceTotal);
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }

    private static DocumentCapture? LoadLiveCapture(ApiDbContext db, int companyId, int captureId) =>
        db.DocumentCaptures.AsNoTracking()
            .SingleOrDefault(c => c.CompanyId == companyId
                && c.Id == captureId
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.DeletedAt == null));

    private static decimal ComputeAuthoritativeBillTotal(ApiDbContext db, IReadOnlyList<Dto.Purchasing.BillLine> lines)
    {
        var taxes = db.Taxes.AsNoTracking().ToDictionary(t => t.Id, t => t.Rate);
        decimal total = 0m;
        foreach (var line in lines)
        {
            var subtotal = PurchaseInvoiceLine.SubtotalOf(line.Quantity, line.Amount, line.Discount);
            var taxRate = line.TaxId.HasValue && taxes.TryGetValue(line.TaxId.Value, out var rate) ? rate : (decimal?)null;
            total += subtotal + PurchaseInvoiceLine.TaxOf(subtotal, taxRate);
        }
        return total;
    }

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
