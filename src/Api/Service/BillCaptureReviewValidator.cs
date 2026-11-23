// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Core.Domain.Purchases;
using Core.Tenancy;
using Dto.DocumentCapture;
using Microsoft.AspNetCore.Mvc;

namespace Api.Service;

/// <summary>
/// Thin adapter over <see cref="CaptureReviewReconciler"/> for bill writers (<c>SaveBill</c> and <c>ApproveBill</c>),
/// computing the authoritative total from the persisted <see cref="PurchaseInvoiceHeader.Total"/>.
/// </summary>
public static class BillCaptureReviewValidator
{
    public const int MaxCaptureReviews = CaptureReviewReconciler.MaxCaptureReviews;

    private static readonly CaptureReviewTargetPolicy Policy = new(
        AllowedKinds: new HashSet<string>(StringComparer.Ordinal)
        {
            "expense-receipt",
            "purchase-invoice",
        },
        TargetType: "bill",
        InvalidKindDetail: "This document capture kind cannot be reviewed for a bill.",
        StateConflictDetail: "Accept the captured fields before approving or attaching them to a bill.",
        DiscrepancyDetail: "The bill total differs from one or more captured source document totals. Correct the bill or explicitly acknowledge each difference before approving.",
        RejectTaxOnlyDifference: true);

    public static IActionResult? ValidateForDraft(
        ApiDbContext db,
        ITenantContext tenantContext,
        int? billId,
        IReadOnlyList<CaptureReviewContext>? reviews) =>
        CaptureReviewReconciler.ValidateForDraft(db, tenantContext, Policy, billId, reviews);

    public static CaptureReviewValidationResult ValidateForApproval(
        ApiDbContext db,
        ITenantContext tenantContext,
        PurchaseInvoiceHeader bill,
        IReadOnlyList<CaptureReviewContext>? reviews) =>
        CaptureReviewReconciler.ValidateForCommit(
            db,
            tenantContext,
            Policy,
            bill.Id > 0 ? bill.Id : null,
            bill.Total(),
            reviews);
}
