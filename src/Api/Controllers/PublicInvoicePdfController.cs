#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Service;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Api.Controllers
{
    /// <summary>
    /// Customer-facing invoice PDF via a signed, email-bound link. Regenerates the PDF on each open; nothing is stored.
    /// </summary>
    [AllowAnonymous]
    [TenantFree]
    [ApiController]
    [Route("api/public/invoices")]
    public class PublicInvoicePdfController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ITenantContext _tenant;
        private readonly SalesInvoicePresentation _invoices;
        private readonly InvoicePdfRenderer _pdfs;
        private readonly ApiDbContext _db;
        private readonly ILogger<PublicInvoicePdfController> _logger;

        public PublicInvoicePdfController(
            IConfiguration configuration,
            ITenantContext tenant,
            SalesInvoicePresentation invoices,
            InvoicePdfRenderer pdfs,
            ApiDbContext db,
            ILogger<PublicInvoicePdfController> logger)
        {
            _configuration = configuration;
            _tenant = tenant;
            _invoices = invoices;
            _pdfs = pdfs;
            _db = db;
            _logger = logger;
        }

        [HttpGet("pdf")]
        [EnableRateLimiting("invoice-pdf-link")]
        public async Task<IActionResult> Pdf(
            [FromQuery] string? c,
            [FromQuery] string? i,
            [FromQuery] string? e,
            [FromQuery] string? exp,
            [FromQuery] string? sig)
        {
            var secret = _configuration["SECRET_KEY"]
                ?? _configuration["SECRET:key"]
                ?? _configuration["SECRET__key"]
                ?? Environment.GetEnvironmentVariable("SECRET_KEY");
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogError("Public invoice PDF requested but SECRET_KEY is not configured.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (!InvoicePdfLinkSigner.TryValidate(c, i, e, exp, sig, secret, DateTimeOffset.UtcNow, out var claims, out var error))
            {
                if (error == "expired")
                    return StatusCode(StatusCodes.Status410Gone, new { message = "This invoice link has expired." });
                return NotFound();
            }

            using (_tenant.Impersonate(claims!.CompanyId))
            {
                var invoice = _invoices.Get(claims.InvoiceId);
                if (invoice == null || invoice.Status is "Draft")
                    return NotFound();

                if (invoice.Status is "Void" || invoice.StatusId == (int)Core.Domain.SalesInvoiceStatus.Void)
                    return StatusCode(StatusCodes.Status410Gone, new { message = "This invoice is no longer available." });

                // Stricter: the live recipient and/or bill-to snapshot must still match the email the link was issued for.
                var allowed = new[] { invoice.RecipientEmail, invoice.CustomerEmail }
                    .Select(InvoicePdfLinkSigner.NormalizeEmail)
                    .Where(email => email != null)
                    .ToHashSet(StringComparer.Ordinal);
                if (!allowed.Contains(claims.Email))
                    return NotFound();

                var currency = _db.CompanySettings.Select(s => s.Currency).FirstOrDefault();
                // Appearance is resolved only after the signature, invoice checks, and organization impersonation.
                var rendered = await _pdfs.RenderAsync(invoice, currency, HttpContext.RequestAborted);

                // Open in the browser (inline), not as a forced download. Never cache financial PDFs.
                var fileName = InvoiceEmailTemplate.PdfFileName(rendered.InvoiceNumber);
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
                Response.Headers.CacheControl = "private, no-store";
                return File(rendered.Pdf, "application/pdf");
            }
        }
    }
}
