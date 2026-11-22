// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests;

public sealed class SalesInvoiceCaptureReviewTests : MultiTenancyPhase3TestBase
{
    public SalesInvoiceCaptureReviewTests(ITestOutputHelper output) : base(output) { }

    private async Task<int> SeedCustomerAsync(string name)
    {
        var res = await Send(HttpMethod.Post, "/api/Sales/SaveClient", new Dto.Sales.Client { Name = name });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var client = await ReadDto<Dto.Sales.Client>(res);
        return client.Id;
    }

    private (int Id, string PublicId) SeedSalesCapture(
        string kind = "sales-invoice",
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
                SourceHash = new string('d', 64),
                ContentHash = new string('e', 64),
                NormalizationVersion = "image-normalization-v2",
                ContentType = "image/png",
                ByteCount = 128,
                Width = 100,
                Height = 100,
                PageCount = 1,
                IdempotencyKey = $"sales-test-{Guid.NewGuid():N}",
                RequestFingerprint = new string('f', 64),
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

    private void SeedInvoiceCaptureLink(int captureId, int invoiceId)
    {
        WithDb(db =>
        {
            db.DocumentCaptureLinks.Add(new DocumentCaptureLink
            {
                CompanyId = _companyId,
                CaptureId = captureId,
                TargetType = "invoice",
                TargetId = invoiceId,
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
                TaxCode = $"ST{Guid.NewGuid().ToString("N")[..4]}",
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

    private static Dto.Sales.SalesInvoice NewInvoice(int customerId, string status, params (string Description, decimal Quantity, decimal Amount, int? TaxId)[] lines) => new()
    {
        CustomerId = customerId,
        InvoiceDate = DateTime.UtcNow.Date,
        Status = status,
        SalesInvoiceLines = lines.Select(l => new Dto.Sales.SalesInvoiceLine
        {
            ItemDescription = l.Description,
            Quantity = l.Quantity,
            Amount = l.Amount,
            TaxId = l.TaxId,
        }).ToList(),
    };

    private Task<HttpResponseMessage> SaveInvoice(Dto.Sales.SalesInvoice invoice) =>
        Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);

    private async Task<Dto.Sales.SalesInvoice> SaveInvoiceOk(Dto.Sales.SalesInvoice invoice)
    {
        var res = await SaveInvoice(invoice);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return await ReadDto<Dto.Sales.SalesInvoice>(res);
    }

    private List<string> ReadInvoiceDiscrepancyAuditMetadata()
    {
        using var scope = _factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        return identityDb.SecurityAuditEvents.AsNoTracking()
            .Where(e => e.CompanyId == _companyId && e.EventType == "invoice.capture_discrepancy.acknowledged")
            .OrderBy(e => e.Id)
            .Select(e => e.MetadataJson ?? string.Empty)
            .ToList();
    }

    [Fact]
    public async Task SaveSalesInvoice_Draft_AllowsUnresolvedDiscrepancyWithoutAcknowledgement()
    {
        var customerId = await SeedCustomerAsync("Draft Sales Client");
        var (captureId, _) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceInvoiceNo":"EXT-100","sourceTotals":{"grandTotal":"110.00","priceBasis":"exclusive"}}""");

        var invoice = NewInvoice(customerId, "Draft", ("Consulting", 1m, 125m, null));
        invoice.ReferenceNo = "EXT-100";
        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 125m,
                DiscrepancyAcknowledged = false,
            }
        ];

        var saved = await SaveInvoiceOk(invoice);
        Assert.Equal("Draft", saved.Status);
        Assert.Equal("EXT-100", saved.ReferenceNo);
        Assert.True(string.IsNullOrWhiteSpace(saved.No));
    }

    [Fact]
    public async Task SaveSalesInvoice_Issue_RefusesUnacknowledgedDiscrepancy_With409DocumentReviewRequired()
    {
        var customerId = await SeedCustomerAsync("Unacked Sales Client");
        var tax10 = SeedTax("Sales VAT 10%", 10m);
        var (captureId, capturePublicId) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        var invoice = NewInvoice(customerId, "Open", ("Consulting", 1m, 110m, tax10));
        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 121m,
                DiscrepancyAcknowledged = false,
            }
        ];

        var res = await SaveInvoice(invoice);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(capturePublicId, doc.RootElement.GetProperty("captureId").GetString());
        Assert.Equal(110m, doc.RootElement.GetProperty("sourceGrandTotal").GetDecimal());
        Assert.Equal(121m, doc.RootElement.GetProperty("formTotal").GetDecimal());
        Assert.Equal(11m, doc.RootElement.GetProperty("difference").GetDecimal());
    }

    [Fact]
    public async Task SaveSalesInvoice_Issue_RefusesStaleAcknowledgement_With409DocumentReviewRequired()
    {
        var customerId = await SeedCustomerAsync("Stale Ack Sales Client");
        var tax10 = SeedTax("Sales VAT 10%", 10m);
        var (captureId, _) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"exclusive"}}""");

        // Line is 120 + 10% tax = 132, but ack is bound to stale formTotal 121.
        var invoice = NewInvoice(customerId, "Open", ("Consulting", 1m, 120m, tax10));
        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 121m,
                DiscrepancyAcknowledged = true,
            }
        ];

        var res = await SaveInvoice(invoice);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(132m, doc.RootElement.GetProperty("formTotal").GetDecimal());
    }

    [Fact]
    public async Task SaveSalesInvoice_Issue_AcceptsCurrentAcknowledgement_AndWritesAuditEvent()
    {
        var customerId = await SeedCustomerAsync("Acked Sales Client");
        var tax10 = SeedTax("Sales VAT 10%", 10m);
        var (captureId, capturePublicId) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceInvoiceNo":"REF-999","sourceTotals":{"grandTotal":"110.00","priceBasis":"exclusive"}}""");

        var invoice = NewInvoice(customerId, "Open", ("Consulting", 1m, 120m, tax10));
        invoice.ReferenceNo = "REF-999";
        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 132m,
                DiscrepancyAcknowledged = true,
            }
        ];

        var issued = await SaveInvoiceOk(invoice);
        Assert.Equal("Open", issued.Status);
        Assert.False(string.IsNullOrWhiteSpace(issued.No));
        Assert.NotEqual("REF-999", issued.No);
        Assert.Equal("REF-999", issued.ReferenceNo);

        var audit = Assert.Single(ReadInvoiceDiscrepancyAuditMetadata());
        Assert.Contains(capturePublicId, audit);
        Assert.Contains(PubId<SalesInvoiceHeader>(issued.Id), audit);
        Assert.Contains("110", audit);
        Assert.Contains("132", audit);
        Assert.Contains("22", audit);
    }

    [Fact]
    public async Task SaveSalesInvoice_Issue_MatchingTotals_SucceedsAndWritesNoDiscrepancyAuditEvent()
    {
        var customerId = await SeedCustomerAsync("Matching Sales Client");
        var tax10 = SeedTax("Sales VAT 10%", 10m);
        var (captureId, _) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"110.00","priceBasis":"inclusive","subtotal":"100.00","taxTotal":"10.00"}}""");

        var invoice = NewInvoice(customerId, "Open", ("Consulting", 1m, 100m, tax10));
        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 110m,
                FormTotal = 110m,
                DiscrepancyAcknowledged = false,
            }
        ];

        var issued = await SaveInvoiceOk(invoice);
        Assert.Equal("Open", issued.Status);
        Assert.Empty(ReadInvoiceDiscrepancyAuditMetadata());
    }

    [Fact]
    public async Task SaveSalesInvoice_PerLineTaxRoundingDifference_SucceedsWhenAcknowledgingServerReturnedTotal()
    {
        var customerId = await SeedCustomerAsync("Rounding Sales Client");
        var tax10 = SeedTax("Sales VAT 10%", 10m);
        var (captureId, _) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"1.09","priceBasis":"exclusive"}}""");

        var invoice = NewInvoice(customerId, "Open", ("A", 1m, 0.33m, tax10), ("B", 1m, 0.33m, tax10), ("C", 1m, 0.33m, tax10));
        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = 1.09m,
                FormTotal = 1.09m,
                DiscrepancyAcknowledged = false,
            }
        ];

        var conflict = await SaveInvoice(invoice);
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

        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext
            {
                CaptureId = captureId,
                SourceGrandTotal = serverSource,
                FormTotal = serverForm,
                DiscrepancyAcknowledged = true,
            }
        ];
        var issued = await SaveInvoiceOk(invoice);
        Assert.Equal("Open", issued.Status);
    }

    [Fact]
    public async Task IssueSalesInvoice_MultipleLinkedDiscrepantCaptures_RequiresAllAcknowledged_AndWritesAuditEvents()
    {
        var customerId = await SeedCustomerAsync("Multi Capture Sales Client");
        var draft = await SaveInvoiceOk(NewInvoice(customerId, "Draft", ("Combined services", 1m, 150m, null)));
        var (cap1Id, cap1Pub) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"100.00","priceBasis":"exclusive"}}""");
        var (cap2Id, cap2Pub) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"60.00","priceBasis":"exclusive"}}""");
        SeedInvoiceCaptureLink(cap1Id, draft.Id);
        SeedInvoiceCaptureLink(cap2Id, draft.Id);

        var partial = new Dto.Sales.IssueSalesInvoiceRequest
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
        var partialRes = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={PubId<SalesInvoiceHeader>(draft.Id)}", partial);
        Assert.Equal(HttpStatusCode.Conflict, partialRes.StatusCode);
        using (var doc = JsonDocument.Parse(await partialRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DOCUMENT_REVIEW_REQUIRED", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(cap2Pub, doc.RootElement.GetProperty("captureId").GetString());
        }

        var full = new Dto.Sales.IssueSalesInvoiceRequest
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
        var okRes = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={PubId<SalesInvoiceHeader>(draft.Id)}", full);
        Assert.True(okRes.IsSuccessStatusCode, await okRes.Content.ReadAsStringAsync());

        var events = ReadInvoiceDiscrepancyAuditMetadata();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, m => m.Contains(cap1Pub));
        Assert.Contains(events, m => m.Contains(cap2Pub));
    }

    [Fact]
    public async Task IssueSalesInvoice_EmptyBodyWithoutCaptures_BehavesAsBefore()
    {
        var customerId = await SeedCustomerAsync("Plain Sales Client");
        var draft = await SaveInvoiceOk(NewInvoice(customerId, "Draft", ("Plain service", 1m, 200m, null)));

        var res = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={PubId<SalesInvoiceHeader>(draft.Id)}");
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        Assert.Empty(ReadInvoiceDiscrepancyAuditMetadata());
    }

    [Fact]
    public async Task SaveSalesInvoice_RejectsCrossCompanyCaptureId_With404UnknownId()
    {
        var customerId = await SeedCustomerAsync("Cross Company Sales Client");
        int otherCompanyId = 0;
        WithDb(db =>
        {
            var otherCo = new Company { Name = "Other Sales Co", CompanyCode = "OSC", Slug = $"other-s-{Guid.NewGuid():N}"[..16] };
            db.Companies.Add(otherCo);
            db.SaveChanges();
            otherCompanyId = otherCo.Id;
        });
        var (_, otherCompanyCapPub) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10"}""",
            companyIdOverride: otherCompanyId);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Sales/SaveSalesInvoice");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);
        req.Headers.Add("X-AccountGo-Org", _slug);
        var payload = $$"""
        {
          "customerId": "{{PubId<Customer>(customerId)}}",
          "invoiceDate": "2026-11-10",
          "status": "Open",
          "salesInvoiceLines": [{ "itemDescription": "Item", "quantity": 1, "amount": 50 }],
          "captureReviews": [{ "captureId": "{{otherCompanyCapPub}}", "discrepancyAcknowledged": true }]
        }
        """;
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("unknown_id", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaveSalesInvoice_RejectsNonSalesCaptureKind_With400DocumentReviewInvalid()
    {
        var customerId = await SeedCustomerAsync("Kind Guard Sales Client");
        var (billCapId, _) = SeedSalesCapture(kind: "expense-receipt", status: "accepted", acceptedFieldsJson: """{"date":"2026-11-10"}""");

        var invoice = NewInvoice(customerId, "Open", ("Item", 1m, 50m, null));
        invoice.CaptureReviews = [new Dto.Purchasing.CaptureReviewContext { CaptureId = billCapId }];
        var res = await SaveInvoice(invoice);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("DOCUMENT_REVIEW_INVALID", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaveSalesInvoice_RejectsUnacceptedCapture_With409DocumentStateConflict()
    {
        var customerId = await SeedCustomerAsync("State Guard Sales Client");
        var (readyCapId, _) = SeedSalesCapture(status: "ready", acceptedFieldsJson: """{"date":"2026-11-10"}""");

        var invoice = NewInvoice(customerId, "Open", ("Item", 1m, 50m, null));
        invoice.CaptureReviews = [new Dto.Purchasing.CaptureReviewContext { CaptureId = readyCapId }];
        var res = await SaveInvoice(invoice);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("DOCUMENT_STATE_CONFLICT", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaveSalesInvoice_RejectsCaptureLinkedToDifferentInvoice_With409DocumentLinkConflict()
    {
        var customerId = await SeedCustomerAsync("Link Guard Sales Client");
        var otherInvoice = await SaveInvoiceOk(NewInvoice(customerId, "Draft", ("Other", 1m, 25m, null)));
        var (linkedCapId, _) = SeedSalesCapture(acceptedFieldsJson: """{"date":"2026-11-10"}""");
        SeedInvoiceCaptureLink(linkedCapId, otherInvoice.Id);

        var invoice = NewInvoice(customerId, "Open", ("Item", 1m, 50m, null));
        invoice.CaptureReviews = [new Dto.Purchasing.CaptureReviewContext { CaptureId = linkedCapId }];
        var res = await SaveInvoice(invoice);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("DOCUMENT_LINK_CONFLICT", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaveSalesInvoice_RejectsDuplicateCaptureIdsInReviews_With400DocumentReviewInvalid()
    {
        var customerId = await SeedCustomerAsync("Dup Guard Sales Client");
        var (capId, _) = SeedSalesCapture(acceptedFieldsJson: """{"date":"2026-11-10"}""");

        var invoice = NewInvoice(customerId, "Open", ("Item", 1m, 50m, null));
        invoice.CaptureReviews =
        [
            new Dto.Purchasing.CaptureReviewContext { CaptureId = capId },
            new Dto.Purchasing.CaptureReviewContext { CaptureId = capId },
        ];
        var res = await SaveInvoice(invoice);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("DOCUMENT_REVIEW_INVALID", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaveSalesInvoice_RejectsNumericCaptureId_With400InvalidId()
    {
        var customerId = await SeedCustomerAsync("Numeric Guard Sales Client");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Sales/SaveSalesInvoice");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);
        req.Headers.Add("X-AccountGo-Org", _slug);
        var payload = $$"""
        {
          "customerId": "{{PubId<Customer>(customerId)}}",
          "invoiceDate": "2026-11-10",
          "status": "Open",
          "salesInvoiceLines": [{ "itemDescription": "Item", "quantity": 1, "amount": 50 }],
          "captureReviews": [{ "captureId": 456, "discrepancyAcknowledged": true }]
        }
        """;
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("invalid_id", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SaveSalesInvoice_Issue_WhenAuditServiceThrows_StillReturns200OpenAndLogsSafeWarning()
    {
        var customerId = await SeedCustomerAsync("Audit Throw Sales Client");
        var (captureId, _) = SeedSalesCapture(
            acceptedFieldsJson: """{"date":"2026-11-10","sourceTotals":{"grandTotal":"100.00","priceBasis":"exclusive"}}""");

        var logSink = new CapturingLoggerProvider();
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<Api.Service.ISecurityAuditService, ThrowingSecurityAuditService>();
                services.AddSingleton<ILoggerProvider>(logSink);
            });
        });
        using var client = customFactory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Sales/SaveSalesInvoice");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);
        req.Headers.Add("X-AccountGo-Org", _slug);
        var payload = $$"""
        {
          "customerId": "{{PubId<Customer>(customerId)}}",
          "invoiceDate": "2026-11-10",
          "status": "Open",
          "salesInvoiceLines": [{ "itemDescription": "Consulting", "quantity": 1, "amount": 145.67 }],
          "captureReviews": [{
            "captureId": "{{PubId<Core.Domain.DocumentCapture>(captureId)}}",
            "sourceGrandTotal": 100.00,
            "formTotal": 145.67,
            "discrepancyAcknowledged": true
          }]
        }
        """;
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var invoicePublicId = doc.RootElement.GetProperty("id").GetString();
        Assert.Equal("Open", doc.RootElement.GetProperty("status").GetString());

        var warning = Assert.Single(logSink.Warnings, m => m.Contains("Failed to record capture discrepancy acknowledgement audit events for invoice"));
        Assert.Contains(invoicePublicId!, warning);
        Assert.Contains("count=1", warning);
        Assert.DoesNotContain("145.67", warning);
        Assert.DoesNotContain("100.00", warning);
    }

    private sealed class ThrowingSecurityAuditService : Api.Service.ISecurityAuditService
    {
        public Task LogEventAsync(string eventType, string? outcome, string? userId, int? companyId, object? metadata = null) =>
            throw new InvalidOperationException("Simulated audit store outage.");

        public Task LogEventAsync(string eventType, string? userId, int? companyId, string? ipAddress, string? userAgent, object? metadata = null) =>
            throw new InvalidOperationException("Simulated audit store outage.");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Warnings { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Warnings);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentBag<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    warnings.Add(formatter(state, exception));
            }
        }
    }
}
