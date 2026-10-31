// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Api.Filters;
using Api.Service;
using Core.Tenancy;
using Dto.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Invoice PDF appearance capabilities and a server-rendered preview for the current organization.</summary>
[Authorize]
[TenantScoped]
[ApiController]
[Route("api/invoice-branding")]
public sealed class InvoiceBrandingController(IInvoiceBrandingCapabilities capabilities, InvoicePdfRenderer pdfs) : ControllerBase
{
    [HttpGet("capabilities")]
    public async Task<IActionResult> GetCapabilities(CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(actor)) return Unauthorized();
        return Ok(await capabilities.GetAsync(actor, ct));
    }

    [HttpGet("preview")]
    public Task<IActionResult> Preview(CancellationToken ct) => RenderPreview(null, ct);

    [HttpPost("preview")]
    public Task<IActionResult> Preview([FromBody] InvoiceBrandingDesired? desired, CancellationToken ct) =>
        RenderPreview(desired, ct);

    private async Task<IActionResult> RenderPreview(InvoiceBrandingDesired? desired, CancellationToken ct)
    {
        try
        {
            var rendered = await pdfs.RenderPreviewAsync(SampleInvoice(), "USD", desired, ct);
            Response.Headers.CacheControl = "private, no-store";
            return File(rendered.Pdf, "application/pdf");
        }
        catch (InvoiceBrandingValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static SalesInvoice SampleInvoice() => new()
    {
        No = "INV-0042",
        CustomerName = "Northwind Traders",
        CustomerAddress = "100 Harbor Street",
        InvoiceDate = new DateTime(2026, 3, 1),
        DueDate = new DateTime(2026, 3, 31),
        Status = "Open",
        ReferenceNo = "PO-18",
        TotalAmountAfterTax = 120m,
        BalanceDue = 120m,
        Notes = "Thank you for your business.",
        Seller = new SellerInfo
        {
            Name = "Sample Seller",
            AddressLines = ["1 Market Street"],
            Email = "billing@example.com"
        },
        SalesInvoiceLines =
        [
            new SalesInvoiceLine { ItemDescription = "Consulting", Quantity = 1, Amount = 120m, LineTotal = 120m }
        ]
    };
}
