// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.DocumentCapture;
using Microsoft.AspNetCore.Mvc;

namespace Api.Service;

/// <summary>
/// Thin adapter over <see cref="CaptureReviewReconciler"/> for sales invoice writers (<c>SaveSalesInvoice</c> and
/// <c>IssueSalesInvoice</c>), computing the authoritative total from the persisted <see cref="SalesInvoiceHeader.GrandTotal"/>.
/// </summary>
public static class SalesInvoiceCaptureReviewValidator
{
    public const int MaxCaptureReviews = CaptureReviewReconciler.MaxCaptureReviews;

    private static readonly CaptureReviewTargetPolicy Policy = new(
        AllowedKinds: new HashSet<string>(StringComparer.Ordinal)
        {
            "sales-invoice",
        },
        TargetType: "invoice",
        InvalidKindDetail: "This document capture kind cannot be reviewed for a sales invoice.",
        StateConflictDetail: "Accept the captured fields before issuing or attaching them to a sales invoice.",
        DiscrepancyDetail: "The invoice total differs from one or more captured source document totals. Correct the invoice or explicitly acknowledge each difference before issuing.");

    public static IActionResult? ValidateForDraft(
        ApiDbContext db,
        ITenantContext tenantContext,
        int? invoiceId,
        IReadOnlyList<CaptureReviewContext>? reviews) =>
        CaptureReviewReconciler.ValidateForDraft(db, tenantContext, Policy, invoiceId, reviews);

    public static CaptureReviewValidationResult ValidateForIssue(
        ApiDbContext db,
        ITenantContext tenantContext,
        SalesInvoiceHeader invoice,
        IReadOnlyList<CaptureReviewContext>? reviews) =>
        CaptureReviewReconciler.ValidateForCommit(
            db,
            tenantContext,
            Policy,
            invoice.Id > 0 ? invoice.Id : null,
            invoice.GrandTotal(),
            reviews);
}
