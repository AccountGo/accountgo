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

    private List<string> ReadDiscrepancyAuditMetadata()
    {
        using var scope = _factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        return identityDb.SecurityAuditEvents.AsNoTracking()
            .Where(e => e.CompanyId == _companyId && e.EventType == "bill.capture_discrepancy.acknowledged")
            .OrderBy(e => e.Id)
            .Select(e => e.MetadataJson ?? string.Empty)
            .ToList();
    }

    [Fact]
    public async Task SaveBill_Draft_AllowsUnresolvedDiscrepancyWithoutAcknowledgement()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Coffee Roasters" });
        var (captureId, _) = SeedCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        var bill = NewBill(vendor.Id, ("Coffee beans", 1m, 125m, null));
        bill.Status = "Draft";
        bill.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 125m,
                DiscrepancyAcknowledged = false,
            }
        ];

        var saved = await SaveBillOk(bill);
        Assert.Equal("Draft", saved.Status);
        Assert.False(saved.Approved);
        Assert.Equal(0m, saved.AmountPaid);
        Assert.Empty(saved.Payments);
    }

    [Fact]
    public async Task SaveBill_Approve_RefusesUnacknowledgedAndStaleDiscrepancy_AcceptsCurrentAndWritesAuditEvent()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Inclusive Supply Co" });
        var tax10 = SeedTax("VAT 10%", 10m);
        var (captureId, capturePublicId) = SeedCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        var bill = NewBill(vendor.Id, ("Supplies", 1m, 110m, null));
        bill.Lines[0].TaxId = tax10;
        bill.Status = "Open";
        bill.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 121m,
                DiscrepancyAcknowledged = false,
            }
        ];

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

        // Stale acknowledgement (user edited line from 110 to 120 => total 132, sent old snapshot 121) is refused.
        bill.Lines[0].Amount = 120m;
        bill.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 121m,
                DiscrepancyAcknowledged = true,
            }
        ];
        var stale = await SaveBill(bill);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var doc = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(132m, doc.RootElement.GetProperty("formTotal").GetDecimal());
        }

        // Current acknowledgement succeeds, leaves AmountPaid = 0 / no payment, and writes audit event.
        bill.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 132m,
                DiscrepancyAcknowledged = true,
            }
        ];
        var approved = await SaveBillOk(bill);
        Assert.True(approved.Approved);
        Assert.Equal(132m, approved.Total);
        Assert.Equal(0m, approved.AmountPaid);
        Assert.Empty(approved.Payments);

        var events = ReadDiscrepancyAuditMetadata();
        var audit = Assert.Single(events);
        Assert.Contains(capturePublicId, audit);
        Assert.Contains(PubId<PurchaseInvoiceHeader>(approved.Id), audit);
        Assert.Contains("110", audit);
        Assert.Contains("132", audit);
        Assert.Contains("22", audit);
    }

    [Fact]
    public async Task SaveBill_Approve_MatchingTotals_SucceedsAndWritesNoDiscrepancyAuditEvent()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Corrected Tax Vendor" });
        var tax10 = SeedTax("VAT 10%", 10m);
        var (captureId, _) = SeedCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        var bill = NewBill(vendor.Id, ("Supplies", 1m, 100m, null));
        bill.Lines[0].TaxId = tax10;
        bill.Status = "Open";
        bill.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 110m,
                DiscrepancyAcknowledged = false,
            }
        ];

        var approved = await SaveBillOk(bill);
        Assert.True(approved.Approved);
        Assert.Equal(110m, approved.Total);
        Assert.Empty(ReadDiscrepancyAuditMetadata());
    }

    [Fact]
    public async Task SaveBill_PerLineTaxRoundingDifference_SucceedsWhenAcknowledgingServerReturnedTotal()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Rounding Vendor" });
        var tax10 = SeedTax("VAT 10%", 10m);
        // 3 lines of 0.33 each: per-line tax = round(0.033) = 0.03 * 3 = 0.09 => bill.Total() = 1.08.
        // Whole-subtotal tax would be round(0.99 * 0.10) = 0.10 => 1.09.
        var (captureId, _) = SeedCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"1.09","priceBasis":"exclusive"}}""");

        var bill = NewBill(vendor.Id, ("A", 1m, 0.33m, null), ("B", 1m, 0.33m, null), ("C", 1m, 0.33m, null));
        foreach (var line in bill.Lines) line.TaxId = tax10;
        bill.Status = "Open";
        // Client initially sends unacknowledged review (or mismatched total).
        bill.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 1.09m,
                FormTotal = 1.09m,
                DiscrepancyAcknowledged = false,
            }
        ];

        var conflict = await SaveBill(bill);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        decimal serverSource, serverForm;
        using (var doc = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
            serverSource = doc.RootElement.GetProperty("sourceGrandTotal").GetDecimal();
            serverForm = doc.RootElement.GetProperty("formTotal").GetDecimal();
            Assert.Equal(1.09m, serverSource);
            Assert.Equal(1.08m, serverForm);
        }

        // Resending with the exact server-reported values succeeds.
        bill.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = serverSource,
                FormTotal = serverForm,
                DiscrepancyAcknowledged = true,
            }
        ];
        var approved = await SaveBillOk(bill);
        Assert.True(approved.Approved);
        Assert.Equal(1.08m, approved.Total);
    }

    [Fact]
    public async Task ApproveBill_MultipleLinkedDiscrepantCaptures_RequiresBothAcknowledged_AndWritesTwoAuditEvents()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Multi Capture Vendor" });
        var draft = await SaveBillOk(NewBill(vendor.Id, ("Combined items", 1m, 150m, null)));
        var (cap1Id, cap1Pub) = SeedCapture(
            kind: "purchase-invoice",
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"100.00","priceBasis":"exclusive"}}""");
        var (cap2Id, cap2Pub) = SeedCapture(
            kind: "expense-receipt",
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"60.00","priceBasis":"exclusive"}}""");
        SeedCaptureLink(cap1Id, draft.Id);
        SeedCaptureLink(cap2Id, draft.Id);

        // Acknowledging only one of the two still returns 409.
        var partialReq = new Dto.Purchasing.ApproveBillRequest
        {
            CaptureReviews =
            [
                new Dto.Purchasing.CaptureReviewContext
                {
                    CaptureId = cap1Id,
                    SourceGrandTotal = 100m,
                    FormTotal = 150m,
                    DiscrepancyAcknowledged = true,
                }
            ]
        };
        var partialRes = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={PubId<PurchaseInvoiceHeader>(draft.Id)}", partialReq);
        Assert.Equal(HttpStatusCode.Conflict, partialRes.StatusCode);
        using (var doc = JsonDocument.Parse(await partialRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(cap2Pub, doc.RootElement.GetProperty("captureId").GetString());
        }

        // Acknowledging both succeeds over HTTP and records both audit events.
        var fullReq = new Dto.Purchasing.ApproveBillRequest
        {
            CaptureReviews =
            [
                new Dto.Purchasing.CaptureReviewContext
                {
                    CaptureId = cap1Id,
                    SourceGrandTotal = 100m,
                    FormTotal = 150m,
                    DiscrepancyAcknowledged = true,
                },
                new Dto.Purchasing.CaptureReviewContext
                {
                    CaptureId = cap2Id,
                    SourceGrandTotal = 60m,
                    FormTotal = 150m,
                    DiscrepancyAcknowledged = true,
                }
            ]
        };
        var okRes = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={PubId<PurchaseInvoiceHeader>(draft.Id)}", fullReq);
        var approved = await BillOk(okRes);
        Assert.True(approved.Approved);
        Assert.Equal(150m, approved.Total);

        var events = ReadDiscrepancyAuditMetadata();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, m => m.Contains(cap1Pub));
        Assert.Contains(events, m => m.Contains(cap2Pub));
    }

    [Fact]
    public async Task ApproveBill_EmptyBodyWithoutCaptures_BehavesAsBefore()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Plain Draft Vendor" });
        var draft = await SaveBillOk(NewBill(vendor.Id, ("Consulting", 1m, 250m, null)));

        var res = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={PubId<PurchaseInvoiceHeader>(draft.Id)}");
        var approved = await BillOk(res);
        Assert.True(approved.Approved);
        Assert.Equal(250m, approved.Total);
        Assert.Empty(ReadDiscrepancyAuditMetadata());
    }

    [Fact]
    public async Task SaveBill_RejectsCrossCompany_WrongKind_Unaccepted_LinkedToOtherBill_DuplicateIds_AndNumericId()
    {
        var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Guard Vendor" });
        var otherBill = await SaveBillOk(NewBill(vendor.Id, ("Other", 1m, 25m, null)));

        var (bankCaptureId, _) = SeedCapture(kind: "bank", status: "accepted", acceptedFieldsJson: """{"name":"Main Checking"}""");
        var (readyCaptureId, _) = SeedCapture(kind: "expense-receipt", status: "ready", acceptedFieldsJson: """{"date":"2026-11-10"}""");
        var (linkedCaptureId, _) = SeedCapture(kind: "expense-receipt", status: "accepted", acceptedFieldsJson: """{"date":"2026-11-10"}""");
        SeedCaptureLink(linkedCaptureId, otherBill.Id);

        // Create a second company and seed a capture there for cross-company check.
        int otherCompanyId = 0;
        WithDb(db =>
        {
            var otherCo = new Company { Name = "Other Co", CompanyCode = "OTH", Slug = $"other-{Guid.NewGuid():N}"[..16] };
            db.Companies.Add(otherCo);
            db.SaveChanges();
            otherCompanyId = otherCo.Id;
        });
        var (_, otherCompanyCapPub) = SeedCapture(
            kind: "expense-receipt",
            status: "accepted",
            acceptedFieldsJson: """{"date":"2026-11-10"}""",
            companyIdOverride: otherCompanyId);

        // 1. Cross-company capture ID returns 404 unknown_id
        using (var crossReq = new HttpRequestMessage(HttpMethod.Post, "/api/Purchasing/SaveBill"))
        {
            crossReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);
            crossReq.Headers.Add("X-AccountGo-Org", _slug);
            var crossPayload = $$"""
            {
              "vendorId": "{{PubId<Vendor>(vendor.Id)}}",
              "date": "2026-11-10",
              "status": "Open",
              "lines": [{ "description": "Item", "quantity": 1, "amount": 50 }],
              "captureReviews": [{ "captureId": "{{otherCompanyCapPub}}", "discrepancyAcknowledged": true }]
            }
            """;
            crossReq.Content = new StringContent(crossPayload, Encoding.UTF8, "application/json");
            var crossRes = await _client.SendAsync(crossReq);
            Assert.Equal(HttpStatusCode.NotFound, crossRes.StatusCode);
            Assert.Contains("unknown_id", await crossRes.Content.ReadAsStringAsync());
        }

        // 2. Non-bill kind returns 400
        var wrongKind = NewBill(vendor.Id, ("Item", 1m, 50m, null));
        wrongKind.Status = "Open";
        wrongKind.CaptureReviews = [new Dto.Purchasing.CaptureReviewContext { CaptureId = bankCaptureId }];
        var wrongKindRes = await SaveBill(wrongKind);
        Assert.Equal(HttpStatusCode.BadRequest, wrongKindRes.StatusCode);

        // 3. Unaccepted capture returns 409
        var notAccepted = NewBill(vendor.Id, ("Item", 1m, 50m, null));
        notAccepted.Status = "Open";
        notAccepted.CaptureReviews = [new Dto.Purchasing.CaptureReviewContext { CaptureId = readyCaptureId }];
        var notAcceptedRes = await SaveBill(notAccepted);
        Assert.Equal(HttpStatusCode.Conflict, notAcceptedRes.StatusCode);

        // 4. Capture linked to a different bill returns 409 DOCUMENT_LINK_CONFLICT
        var linkedElsewhere = NewBill(vendor.Id, ("Item", 1m, 50m, null));
        linkedElsewhere.Status = "Open";
        linkedElsewhere.CaptureReviews = [new Dto.Purchasing.CaptureReviewContext { CaptureId = linkedCaptureId }];
        var linkedElsewhereRes = await SaveBill(linkedElsewhere);
        Assert.Equal(HttpStatusCode.Conflict, linkedElsewhereRes.StatusCode);
        Assert.Contains("DOCUMENT_LINK_CONFLICT", await linkedElsewhereRes.Content.ReadAsStringAsync());

        // 5. Duplicate capture IDs return 400
        var (okCapId, _) = SeedCapture(kind: "expense-receipt", status: "accepted", acceptedFieldsJson: """{"date":"2026-11-10"}""");
        var dupReviews = NewBill(vendor.Id, ("Item", 1m, 50m, null));
        dupReviews.Status = "Open";
        dupReviews.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext { CaptureId = okCapId },
            new Dto.Purchasing.CaptureReviewContext { CaptureId = okCapId },
        ];
        var dupRes = await SaveBill(dupReviews);
        Assert.Equal(HttpStatusCode.BadRequest, dupRes.StatusCode);

        // 6. Raw numeric captureId fails shared public-ID validation with 400 invalid_id
        using var numReq = new HttpRequestMessage(HttpMethod.Post, "/api/Purchasing/SaveBill");
        numReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);
        numReq.Headers.Add("X-AccountGo-Org", _slug);
        var numPayload = $$"""
        {
          "vendorId": "{{PubId<Vendor>(vendor.Id)}}",
          "date": "2026-11-10",
          "status": "Open",
          "lines": [{ "description": "Item", "quantity": 1, "amount": 50 }],
          "captureReviews": [{ "captureId": 123, "discrepancyAcknowledged": true }]
        }
        """;
        numReq.Content = new StringContent(numPayload, Encoding.UTF8, "application/json");
        var numericRes = await _client.SendAsync(numReq);
        Assert.Equal(HttpStatusCode.BadRequest, numericRes.StatusCode);
        Assert.Contains("invalid_id", await numericRes.Content.ReadAsStringAsync());
    }
}
