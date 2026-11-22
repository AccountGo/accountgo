// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests;

public sealed class PurchasingBillCaptureReviewTests : MultiTenancyPhase3TestBase
{
    public PurchasingBillCaptureReviewTests(ITestOutputHelper output) : base(output) { }

    private (int Id, string PublicId) SeedCapture(
        string kind = "expense-receipt",
        string status = "accepted",
        string? acceptedFieldsJson = null,
        int? companyIdOverride = null)
    {
        var id = 0;
        var publicId = string.Empty;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var targetCompanyId = companyIdOverride ?? _companyId;
        var targetSlug = companyIdOverride.HasValue ? "other-co" : _slug;
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(targetCompanyId, targetSlug, CompanyMemberRole.Owner))
        {
            var now = DateTime.UtcNow;
            var capture = new Core.Domain.DocumentCapture
            {
                CompanyId = targetCompanyId,
                Kind = kind,
                SchemaVersion = 1,
                Status = status,
                SourceHash = new string('a', 64),
                ContentHash = new string('b', 64),
                NormalizationVersion = "image-normalization-v2",
                ContentType = "image/png",
                ByteCount = 128,
                Width = 100,
                Height = 100,
                PageCount = 1,
                IdempotencyKey = $"test-{Guid.NewGuid():N}",
                RequestFingerprint = new string('c', 64),
                ExtractionGeneration = 1,
                FieldsJson = status == "accepted" ? null : (acceptedFieldsJson ?? "{}"),
                AcceptedFieldsJson = status == "accepted" ? acceptedFieldsJson : null,
                ProviderName = "fake",
                ProviderVersion = "1",
                CreatedBy = "tester",
                CreatedAt = now,
                AcceptedBy = status == "accepted" ? "tester" : null,
                AcceptedAt = status == "accepted" ? now : null,
                ExpiresAt = now.AddDays(7),
                Blob = new DocumentCaptureBlob
                {
                    CompanyId = targetCompanyId,
                    DocumentBytes = [0x89, 0x50, 0x4E, 0x47],
                },
            };
            db.DocumentCaptures.Add(capture);
            db.SaveChanges();
            id = capture.Id;
            publicId = capture.PublicId;
        }
        return (id, publicId);
    }

    private void SeedCaptureLink(int captureId, int billId)
    {
        WithDb(db =>
        {
            db.DocumentCaptureLinks.Add(new DocumentCaptureLink
            {
                CompanyId = _companyId,
                CaptureId = captureId,
                TargetType = "bill",
                TargetId = billId,
                LinkedBy = "tester",
                LinkedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
        });
    }

    private int SeedTax(string name, decimal rate)
    {
        var taxId = 0;
        WithDb(db =>
        {
            var tax = new Tax
            {
                CompanyId = _companyId,
                TaxCode = $"TX{Guid.NewGuid().ToString("N")[..4]}",
                TaxName = name,
                Rate = rate,
                IsActive = true,
            };
            db.Taxes.Add(tax);
            db.SaveChanges();
            taxId = tax.Id;
        });
        return taxId;
    }

    [Fact]
    public async Task SaveBill_Draft_AllowsUnresolvedDiscrepancyWithoutAcknowledgement()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Coffee Roasters" });
        var (captureId, _) = SeedCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        var bill = NewBill(vendor.Id, ("Coffee beans", 1m, 125m, null));
        bill.Status = "Draft";
        bill.CaptureReview = new Dto.Purchasing.CaptureReviewContext
        {
            CaptureId = captureId,
            SourceGrandTotal = 110m,
            FormTotal = 125m,
            DiscrepancyAcknowledged = false,
        };

        var saved = await SaveBillOk(bill);
        Assert.Equal("Draft", saved.Status);
        Assert.False(saved.Approved);
        Assert.Empty(saved.Payments);
    }

    [Fact]
    public async Task SaveBill_Approve_RefusesUnacknowledgedTaxInclusiveDiscrepancy_AndAcceptsCurrentAcknowledgement()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Inclusive Supply Co" });
        var tax10 = SeedTax("VAT 10%", 10m);
        // Source receipt total is 110.00 (100.00 + 10.00 tax inclusive).
        // If user mistakenly enters 110.00 unit cost + 10% tax => authoritative bill total is 121.00.
        var (captureId, capturePublicId) = SeedCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        var bill = NewBill(vendor.Id, ("Supplies", 1m, 110m, null));
        bill.Lines[0].TaxId = tax10;
        bill.Status = "Open";
        bill.CaptureReview = new Dto.Purchasing.CaptureReviewContext
        {
            CaptureId = captureId,
            SourceGrandTotal = 110m,
            FormTotal = 121m,
            DiscrepancyAcknowledged = false,
        };

        var refused = await SaveBill(bill);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using (var doc = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(capturePublicId, doc.RootElement.GetProperty("captureId").GetString());
            Assert.Equal(110m, doc.RootElement.GetProperty("sourceGrandTotal").GetDecimal());
            Assert.Equal(121m, doc.RootElement.GetProperty("formTotal").GetDecimal());
            Assert.Equal(11m, doc.RootElement.GetProperty("difference").GetDecimal());
        }

        // Stale acknowledgement (user edited line from 110 to 120 => total 132, but sent old snapshot 121) is refused.
        bill.Lines[0].Amount = 120m;
        bill.CaptureReview = new Dto.Purchasing.CaptureReviewContext
        {
            CaptureId = captureId,
            SourceGrandTotal = 110m,
            FormTotal = 121m,
            DiscrepancyAcknowledged = true,
        };
        var stale = await SaveBill(bill);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var doc = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(132m, doc.RootElement.GetProperty("formTotal").GetDecimal());
        }

        // Current acknowledgement bound to (captureId, 110.00, 132.00) succeeds and creates no payment.
        bill.CaptureReview = new Dto.Purchasing.CaptureReviewContext
        {
            CaptureId = captureId,
            SourceGrandTotal = 110m,
            FormTotal = 132m,
            DiscrepancyAcknowledged = true,
        };
        var approved = await SaveBillOk(bill);
        Assert.True(approved.Approved);
        Assert.Equal(132m, approved.Total);
        Assert.Equal(0m, approved.AmountPaid);
        Assert.Empty(approved.Payments);
    }

    [Fact]
    public async Task SaveBill_Approve_MatchingCorrectedTaxExclusiveTotal_SucceedsWithoutDiscrepancyFlag()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Corrected Tax Vendor" });
        var tax10 = SeedTax("VAT 10%", 10m);
        var (captureId, _) = SeedCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        // Corrected: 100.00 unit cost + 10% tax = 110.00 authoritative total matches 110.00 source total.
        var bill = NewBill(vendor.Id, ("Supplies", 1m, 100m, null));
        bill.Lines[0].TaxId = tax10;
        bill.Status = "Open";
        bill.CaptureReview = new Dto.Purchasing.CaptureReviewContext
        {
            CaptureId = captureId,
            SourceGrandTotal = 110m,
            FormTotal = 110m,
            DiscrepancyAcknowledged = false,
        };

        var approved = await SaveBillOk(bill);
        Assert.True(approved.Approved);
        Assert.Equal(110m, approved.Total);
    }

    [Fact]
    public async Task ApproveBill_WithLinkedCaptureDiscrepancy_RefusesUnacknowledged_AndAcceptsBoundAcknowledgement()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Linked Draft Vendor" });
        var draft = await SaveBillOk(NewBill(vendor.Id, ("Freight & items", 1m, 95m, null)));
        var (captureId, capturePublicId) = SeedCapture(
            kind: "purchase-invoice",
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"100.00","priceBasis":"exclusive"}}""");
        SeedCaptureLink(captureId, draft.Id);

        // Empty-body ApproveBill is refused because linked capture has a discrepancy (95 vs 100).
        var refused = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={PubId<PurchaseInvoiceHeader>(draft.Id)}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using (var doc = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(capturePublicId, doc.RootElement.GetProperty("captureId").GetString());
        }

        // Approving with current bound acknowledgement succeeds.
        var ack = new Dto.Purchasing.CaptureReviewContext
        {
            CaptureId = captureId,
            SourceGrandTotal = 100m,
            FormTotal = 95m,
            DiscrepancyAcknowledged = true,
        };
        var ok = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={PubId<PurchaseInvoiceHeader>(draft.Id)}", ack);
        var approved = await BillOk(ok);
        Assert.True(approved.Approved);
        Assert.Equal(95m, approved.Total);
    }

    [Fact]
    public async Task SaveBill_RejectsWrongKind_UnacceptedCapture_AndNumericCaptureId()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Guard Vendor" });
        var (bankCaptureId, _) = SeedCapture(kind: "bank", status: "accepted", acceptedFieldsJson: """{"name":"Main Checking"}""");
        var (readyCaptureId, _) = SeedCapture(kind: "expense-receipt", status: "ready", acceptedFieldsJson: """{"date":"2026-11-10"}""");

        var wrongKind = NewBill(vendor.Id, ("Item", 1m, 50m, null));
        wrongKind.Status = "Open";
        wrongKind.CaptureReview = new Dto.Purchasing.CaptureReviewContext { CaptureId = bankCaptureId };
        var wrongKindRes = await SaveBill(wrongKind);
        Assert.Equal(HttpStatusCode.BadRequest, wrongKindRes.StatusCode);

        var notAccepted = NewBill(vendor.Id, ("Item", 1m, 50m, null));
        notAccepted.Status = "Open";
        notAccepted.CaptureReview = new Dto.Purchasing.CaptureReviewContext { CaptureId = readyCaptureId };
        var notAcceptedRes = await SaveBill(notAccepted);
        Assert.Equal(HttpStatusCode.Conflict, notAcceptedRes.StatusCode);

        // Raw numeric captureId must be rejected by the shared PublicId serializer.
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Purchasing/SaveBill");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);
        req.Headers.Add("X-AccountGo-Org", _slug);
        var payload = $$"""
        {
          "vendorId": "{{PubId<Vendor>(vendor.Id)}}",
          "date": "2026-11-10",
          "status": "Open",
          "lines": [{ "description": "Item", "quantity": 1, "amount": 50 }],
          "captureReview": { "captureId": 123, "discrepancyAcknowledged": true }
        }
        """;
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var numericRes = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, numericRes.StatusCode);
        Assert.Contains("invalid_id", await numericRes.Content.ReadAsStringAsync());
    }
}
