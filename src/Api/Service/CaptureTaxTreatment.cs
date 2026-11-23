// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Service;

/// <summary>
/// Derives how a scanned document's tax was treated on the bill or invoice it is linked to, from the saved document and
/// the workspace's tax regime. The value is metadata only (no document text) and is never taken from the client.
/// </summary>
public static class CaptureTaxTreatment
{
    public const string None = "none";
    public const string InCost = "in-cost";
    public const string Acknowledged = "acknowledged";
    public const string RatePrefix = "tax-rate:";
    public const string MixedRates = "tax-rate:mixed";

    private const decimal Cent = 0.01m;

    /// <summary>
    /// <c>tax-rate:&lt;tax public id&gt;</c> when every line carries one tax, <c>tax-rate:mixed</c> when lines carry different taxes,
    /// <c>in-cost</c> for a bill with no line tax whose total equals the document's grand total, <c>acknowledged</c> for an invoice
    /// in a workspace without tax whose total is short of the document by its tax, <c>none</c> when the document had no tax,
    /// and null when none of these apply yet (for example a draft still being corrected).
    /// </summary>
    public static string? Derive(
        string? acceptedFieldsJson,
        string targetType,
        IReadOnlyList<string?> linePublicTaxIds,
        decimal targetTotal,
        TaxRegime regime)
    {
        var sourceTax = CaptureReviewReconciler.ExtractCanonicalSourceTaxTotal(acceptedFieldsJson);
        if (sourceTax is null or <= 0m)
            return None;

        if (linePublicTaxIds.Any(id => id is not null))
        {
            var distinct = linePublicTaxIds.Distinct(StringComparer.Ordinal).ToList();
            return distinct.Count == 1 && distinct[0] is { } only ? RatePrefix + only : MixedRates;
        }

        var sourceTotal = CaptureReviewReconciler.ExtractCanonicalSourceGrandTotal(acceptedFieldsJson);
        if (sourceTotal is null)
            return null;

        if (targetType == "bill" && Math.Abs(targetTotal - sourceTotal.Value) <= Cent)
            return InCost;

        if (targetType == "invoice" && regime == TaxRegime.None && Math.Abs((sourceTotal.Value - targetTotal) - sourceTax.Value) <= Cent)
            return Acknowledged;

        return null;
    }

    /// <summary>Re-derives the treatment on every capture link of one bill or invoice after it is saved. Caller saves the context's transaction.</summary>
    public static void RefreshForTarget(ApiDbContext db, ITenantContext tenant, string targetType, int targetId)
    {
        var companyId = tenant.RequireCompanyId();
        var links = db.DocumentCaptureLinks
            .Where(link => link.CompanyId == companyId && link.TargetType == targetType && link.TargetId == targetId)
            .ToList();
        if (links.Count == 0)
            return;

        var regime = db.CompanySettings.AsNoTracking()
            .Where(setting => setting.CompanyId == companyId)
            .Select(setting => (TaxRegime?)setting.TaxRegime)
            .FirstOrDefault() ?? TaxRegime.None;

        List<string?> taxIds;
        decimal total;
        if (targetType == "bill")
        {
            var bill = db.PurchaseInvoiceHeaders.AsNoTracking().Include(b => b.PurchaseInvoiceLines)
                .SingleOrDefault(b => b.CompanyId == companyId && b.Id == targetId);
            if (bill is null)
                return;
            total = Math.Round(bill.Total(), 2, MidpointRounding.AwayFromZero);
            taxIds = TaxPublicIds(db, companyId, bill.PurchaseInvoiceLines.Select(l => l.TaxId));
        }
        else
        {
            var invoice = db.SalesInvoiceHeaders.AsNoTracking().Include(i => i.SalesInvoiceLines)
                .SingleOrDefault(i => i.CompanyId == companyId && i.Id == targetId);
            if (invoice is null)
                return;
            total = invoice.GrandTotal();
            taxIds = TaxPublicIds(db, companyId, invoice.SalesInvoiceLines.Select(l => l.TaxId));
        }

        var captureIds = links.Select(l => l.CaptureId).ToList();
        var fieldsById = db.DocumentCaptures.AsNoTracking()
            .Where(c => c.CompanyId == companyId && captureIds.Contains(c.Id))
            .Select(c => new { c.Id, c.AcceptedFieldsJson })
            .ToDictionary(c => c.Id, c => c.AcceptedFieldsJson);

        foreach (var link in links)
        {
            fieldsById.TryGetValue(link.CaptureId, out var fields);
            link.TaxTreatment = Derive(fields, targetType, taxIds, total, regime);
        }
        db.SaveChanges();
    }

    private static List<string?> TaxPublicIds(ApiDbContext db, int companyId, IEnumerable<int?> lineTaxIds)
    {
        var lineIds = lineTaxIds.ToList();
        var distinct = lineIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var publicIds = distinct.Count == 0
            ? new Dictionary<int, string>()
            : db.Taxes.AsNoTracking().Where(t => t.CompanyId == companyId && distinct.Contains(t.Id))
                .Select(t => new { t.Id, t.PublicId }).ToDictionary(t => t.Id, t => t.PublicId);
        return lineIds.Select(id => id.HasValue && publicIds.TryGetValue(id.Value, out var pid) ? pid : null).ToList();
    }
}
