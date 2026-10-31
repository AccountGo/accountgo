// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Threading;
using System.Threading.Tasks;
using QuestPDF.Fluent;

namespace Api.Service;

/// <summary>A generated invoice PDF and the appearance that produced it.</summary>
public sealed record InvoicePdfRenderResult(byte[] Pdf, string InvoiceNumber, InvoicePdfAppearance Appearance, string AppearanceStamp);

/// <summary>Resolves appearance once, then constructs <see cref="InvoicePdfDocument"/>.</summary>
public sealed class InvoicePdfRenderer(IInvoicePdfAppearanceProvider appearances)
{
    public async Task<InvoicePdfRenderResult> RenderAsync(Dto.Sales.SalesInvoice invoice, string? currencyCode, CancellationToken ct = default) =>
        Render(invoice, currencyCode, await appearances.GetEffectiveAsync(ct));

    public Task<InvoicePdfRenderResult> RenderPreviewAsync(Dto.Sales.SalesInvoice invoice, string? currencyCode, InvoiceBrandingDesired? desired, CancellationToken ct = default) =>
        RenderPreview(invoice, currencyCode, appearances, desired, ct);

    public static InvoicePdfRenderResult Render(Dto.Sales.SalesInvoice invoice, string? currencyCode, InvoicePdfAppearance appearance)
    {
        var document = new InvoicePdfDocument(invoice, currencyCode, appearance);
        return new(GenerateExtensions.GeneratePdf(document), document.InvoiceNumber, appearance, InvoicePdfAppearanceStamp.Serialize(appearance));
    }

    private static async Task<InvoicePdfRenderResult> RenderPreview(Dto.Sales.SalesInvoice invoice, string? currencyCode, IInvoicePdfAppearanceProvider appearances, InvoiceBrandingDesired? desired, CancellationToken ct) =>
        Render(invoice, currencyCode, await appearances.PreviewAsync(desired, ct));
}
