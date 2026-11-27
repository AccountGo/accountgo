// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Api.PublicIds;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Dto.DocumentCapture;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AccountGo.BackendTests;

public sealed class CaptureReviewReconcilerTests : IDisposable
{
    private static readonly CaptureReviewTargetPolicy TestPolicy = new(
        AllowedKinds: new HashSet<string>(StringComparer.Ordinal) { "expense-receipt", "purchase-invoice" },
        TargetType: "bill",
        InvalidKindDetail: "Invalid kind for target.",
        StateConflictDetail: "Capture must be accepted.",
        DiscrepancyDetail: "Totals differ.");

    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly StubTenantContext _tenant;
    private readonly int _companyId;
    private readonly int _otherCompanyId;

    public CaptureReviewReconcilerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _tenant = new StubTenantContext();
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApiDbContext(options, _tenant) { SuppressTenantSecurityCheck = true };
        _db.Database.EnsureCreated();

        var company = new Company { Name = "Primary Co", Slug = "primary-co", CreatedAt = DateTime.UtcNow };
        var other = new Company { Name = "Other Co", Slug = "other-co", CreatedAt = DateTime.UtcNow };
        _db.Companies.AddRange(company, other);
        _db.SaveChanges();

        _companyId = company.Id;
        _otherCompanyId = other.Id;
        _tenant.CompanyId = _companyId;
        _tenant.CompanySlug = company.Slug;
        _tenant.UserId = "user-1";
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Core.Domain.DocumentCapture SeedCapture(
        string kind = "expense-receipt",
        string status = "accepted",
        string? acceptedFieldsJson = "{\"sourceTotals\":{\"grandTotal\":\"100.00\"}}",
        int? companyId = null)
    {
        var now = DateTime.UtcNow;
        var capture = new Core.Domain.DocumentCapture
        {
            CompanyId = companyId ?? _companyId,
            Kind = kind,
            SchemaVersion = 1,
            Status = status,
            SourceHash = new string('a', 64),
            ContentHash = new string('b', 64),
            NormalizationVersion = "image-normalization-v2",
            ContentType = "image/png",
            ByteCount = 64,
            Width = 100,
            Height = 100,
            PageCount = 1,
            IdempotencyKey = $"key-{Guid.NewGuid():N}",
            RequestFingerprint = new string('c', 64),
            ExtractionGeneration = 1,
            ProviderName = "fake",
            ProviderVersion = "1",
            FieldsJson = status == "accepted" ? null : (acceptedFieldsJson ?? "{}"),
            AcceptedFieldsJson = status == "accepted" ? acceptedFieldsJson : null,
            WarningsJson = "[]",
            CreatedBy = "user-1",
            CreatedAt = now,
            AcceptedBy = status == "accepted" ? "user-1" : null,
            AcceptedAt = status == "accepted" ? now : null,
            ExpiresAt = now.AddDays(7),
        };
        _db.DocumentCaptures.Add(capture);
        _db.SaveChanges();
        return capture;
    }

    private void SeedLink(int captureId, string targetType, int targetId, int? companyId = null)
    {
        _db.DocumentCaptureLinks.Add(new DocumentCaptureLink
        {
            CompanyId = companyId ?? _companyId,
            CaptureId = captureId,
            TargetType = targetType,
            TargetId = targetId,
            LinkedBy = "user-1",
            LinkedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();
    }

    [Fact]
    public void ValidateForDraft_NullOrEmptyReviews_ReturnsNull()
    {
        Assert.Null(CaptureReviewReconciler.ValidateForDraft(_db, _tenant, TestPolicy, null, null));
        Assert.Null(CaptureReviewReconciler.ValidateForDraft(_db, _tenant, TestPolicy, null, Array.Empty<CaptureReviewContext>()));
    }

    [Fact]
    public void ValidateForDraft_ExceedingMaxCaptureReviews_Returns400()
    {
        var reviews = Enumerable.Range(1, CaptureReviewReconciler.MaxCaptureReviews + 1)
            .Select(i => new CaptureReviewContext { CaptureId = i })
            .ToList();

        var result = CaptureReviewReconciler.ValidateForDraft(_db, _tenant, TestPolicy, null, reviews);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal("DOCUMENT_REVIEW_INVALID", problem.Extensions["code"]);
    }

    [Fact]
    public void ValidateForDraft_InvalidCaptureId_Returns400()
    {
        var result = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, null, [new CaptureReviewContext { CaptureId = 0 }]);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal("DOCUMENT_REVIEW_INVALID", problem.Extensions["code"]);
    }

    [Fact]
    public void ValidateForDraft_DuplicateCaptureIds_Returns400()
    {
        var cap = SeedCapture();

        var result = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, null, [
                new CaptureReviewContext { CaptureId = cap.Id },
                new CaptureReviewContext { CaptureId = cap.Id },
            ]);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal("DOCUMENT_REVIEW_INVALID", problem.Extensions["code"]);
    }

    [Fact]
    public void ValidateForDraft_UnknownOrCrossTenantCapture_Returns404()
    {
        var otherCap = SeedCapture(companyId: _otherCompanyId);

        var result = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, null, [new CaptureReviewContext { CaptureId = otherCap.Id }]);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void ValidateForDraft_DisallowedKind_Returns400()
    {
        var cap = SeedCapture(kind: "sales-invoice");

        var result = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, null, [new CaptureReviewContext { CaptureId = cap.Id }]);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal("DOCUMENT_REVIEW_INVALID", problem.Extensions["code"]);
        Assert.Equal(cap.PublicId, problem.Extensions["captureId"]);
    }

    [Fact]
    public void ValidateForDraft_UnacceptedCapture_Returns409StateConflict()
    {
        var cap = SeedCapture(status: "ready");

        var result = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, null, [new CaptureReviewContext { CaptureId = cap.Id }]);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal("DOCUMENT_STATE_CONFLICT", problem.Extensions["code"]);
    }

    [Fact]
    public void ValidateForDraft_LinkedToAnotherTargetOrWhenNewRecord_Returns409LinkConflict()
    {
        var cap = SeedCapture();
        SeedLink(cap.Id, "bill", 42);

        var newRecordResult = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, null, [new CaptureReviewContext { CaptureId = cap.Id }]);
        var differentTargetResult = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, 99, [new CaptureReviewContext { CaptureId = cap.Id }]);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(newRecordResult).StatusCode);
        Assert.Equal("DOCUMENT_LINK_CONFLICT", Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(differentTargetResult).Value).Extensions["code"]);
    }

    [Fact]
    public void ValidateForDraft_LinkedToSameTarget_ReturnsNull()
    {
        var cap = SeedCapture();
        SeedLink(cap.Id, "bill", 42);

        var result = CaptureReviewReconciler.ValidateForDraft(
            _db, _tenant, TestPolicy, 42, [new CaptureReviewContext { CaptureId = cap.Id }]);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateForCommit_MatchingAuthoritativeTotal_SucceedsWithNoAcknowledgements()
    {
        var cap = SeedCapture(acceptedFieldsJson: "{\"sourceTotals\":{\"grandTotal\":\"123.45\"}}");

        var result = CaptureReviewReconciler.ValidateForCommit(
            _db, _tenant, TestPolicy, null, 123.45m, [new CaptureReviewContext { CaptureId = cap.Id }]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.AcknowledgedDiscrepancies);
    }

    [Fact]
    public void ValidateForCommit_UnacknowledgedDiscrepancy_Returns409WithDiscrepancies()
    {
        var cap = SeedCapture(acceptedFieldsJson: "{\"sourceTotals\":{\"grandTotal\":\"100.00\"}}");

        var result = CaptureReviewReconciler.ValidateForCommit(
            _db, _tenant, TestPolicy, null, 115.50m, [new CaptureReviewContext { CaptureId = cap.Id }]);

        Assert.False(result.Succeeded);
        var obj = Assert.IsType<ObjectResult>(result.Failure);
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal("DOCUMENT_REVIEW_REQUIRED", problem.Extensions["code"]);
        Assert.Equal(cap.PublicId, problem.Extensions["captureId"]);
        Assert.Equal(100.00m, problem.Extensions["sourceGrandTotal"]);
        Assert.Equal(115.50m, problem.Extensions["formTotal"]);
        Assert.Equal(15.50m, problem.Extensions["difference"]);
    }

    [Fact]
    public void ValidateForCommit_StaleAcknowledgement_Returns409()
    {
        var cap = SeedCapture(acceptedFieldsJson: "{\"sourceTotals\":{\"grandTotal\":\"100.00\"}}");

        var result = CaptureReviewReconciler.ValidateForCommit(
            _db, _tenant, TestPolicy, null, 120.00m, [
                new CaptureReviewContext
                {
                    CaptureId = cap.Id,
                    SourceGrandTotal = 100.00m,
                    FormTotal = 115.00m,
                    DiscrepancyAcknowledged = true,
                },
            ]);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(result.Failure).StatusCode);
    }

    [Fact]
    public void ValidateForCommit_CurrentAcknowledgement_SucceedsAndReturnsAcknowledgedRecord()
    {
        var cap = SeedCapture(acceptedFieldsJson: "{\"sourceTotals\":{\"grandTotal\":\"100.00\"}}");

        var result = CaptureReviewReconciler.ValidateForCommit(
            _db, _tenant, TestPolicy, null, 120.00m, [
                new CaptureReviewContext
                {
                    CaptureId = cap.Id,
                    SourceGrandTotal = 100.00m,
                    FormTotal = 120.00m,
                    DiscrepancyAcknowledged = true,
                },
            ]);

        Assert.True(result.Succeeded);
        var ack = Assert.Single(result.AcknowledgedDiscrepancies);
        Assert.Equal(cap.PublicId, ack.CapturePublicId);
        Assert.Equal(100.00m, ack.SourceGrandTotal);
        Assert.Equal(120.00m, ack.FormTotal);
        Assert.Equal(20.00m, ack.Difference);
    }

    [Fact]
    public void ValidateForCommit_LinkedCaptureCheckedWhenReviewsOmitted_Returns409OnDiscrepancy()
    {
        var cap = SeedCapture(acceptedFieldsJson: "{\"sourceTotals\":{\"grandTotal\":\"80.00\"}}");
        SeedLink(cap.Id, "bill", 77);

        var result = CaptureReviewReconciler.ValidateForCommit(
            _db, _tenant, TestPolicy, 77, 90.00m, null);

        Assert.False(result.Succeeded);
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result.Failure).Value);
        Assert.Equal("DOCUMENT_REVIEW_REQUIRED", problem.Extensions["code"]);
        Assert.Equal(cap.PublicId, problem.Extensions["captureId"]);
    }

    [Fact]
    public void ValidateForCommit_CaptureWithoutSourceGrandTotal_SkipsReconciliation()
    {
        var cap = SeedCapture(acceptedFieldsJson: "{\"vendorName\":\"Acme\"}");

        var result = CaptureReviewReconciler.ValidateForCommit(
            _db, _tenant, TestPolicy, null, 999.00m, [new CaptureReviewContext { CaptureId = cap.Id }]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.AcknowledgedDiscrepancies);
    }

    [Fact]
    public void RecordDiscrepancyAcknowledgements_LogsParameterizedEventAndTargetIdField()
    {
        var audit = new RecordingSecurityAuditService();
        var logger = new RecordingLogger();
        var acks = new[] { new AcknowledgedCaptureDiscrepancy("cap_abc", 100.00m, 110.00m, 10.00m) };

        CaptureReviewReconciler.RecordDiscrepancyAcknowledgements(
            audit, logger, _tenant, "fallback",
            "invoice.capture_discrepancy.acknowledged", "invoice", "invoiceId", "inv_123", acks);

        var entry = Assert.Single(audit.Events);
        Assert.Equal("invoice.capture_discrepancy.acknowledged", entry.EventType);
        Assert.Equal("success", entry.Outcome);
        Assert.Equal("user-1", entry.UserId);
        Assert.Equal(_companyId, entry.CompanyId);
        var details = Assert.IsType<Dictionary<string, object?>>(entry.Details);
        Assert.Equal("inv_123", details["invoiceId"]);
        Assert.Equal("cap_abc", details["captureId"]);
        Assert.Equal(100.00m, details["sourceGrandTotal"]);
        Assert.Equal(110.00m, details["formTotal"]);
        Assert.Equal(10.00m, details["difference"]);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void RecordDiscrepancyAcknowledgements_AuditThrows_SwallowsAndLogsWarningWithoutAmounts()
    {
        var audit = new ThrowingSecurityAuditService();
        var logger = new RecordingLogger();
        var acks = new[] { new AcknowledgedCaptureDiscrepancy("cap_abc", 123.45m, 678.90m, 555.45m) };

        CaptureReviewReconciler.RecordDiscrepancyAcknowledgements(
            audit, logger, _tenant, "fallback",
            "bill.capture_discrepancy.acknowledged", "bill", "billId", "bill_999", acks);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("bill_999", warning);
        Assert.Contains("count=1", warning);
        Assert.DoesNotContain("123.45", warning);
        Assert.DoesNotContain("678.90", warning);
        Assert.DoesNotContain("555.45", warning);
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public int? CompanyId { get; set; }
        public string? CompanySlug { get; set; }
        public string? UserId { get; set; }
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
        public bool IsResolved => CompanyId.HasValue;
        public bool IsSuperAdmin => false;
        public bool IsPersonalCompany => false;
        public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No company");
        public IDisposable Impersonate(int companyId, string? companySlug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null)
            => throw new NotSupportedException();
    }

    private sealed class RecordingSecurityAuditService : ISecurityAuditService
    {
        public List<(string EventType, string? Outcome, string? UserId, int? CompanyId, object? Details)> Events { get; } = new();

        public Task LogEventAsync(string eventType, string? userId = null, int? companyId = null, string? ipAddress = null, string? userAgent = null, object? details = null)
        {
            Events.Add((eventType, "success", userId, companyId, details));
            return Task.CompletedTask;
        }

        public Task LogEventAsync(string eventType, string? outcome, string? userId, int? companyId, object? details)
        {
            Events.Add((eventType, outcome, userId, companyId, details));
            return Task.CompletedTask;
        }

        public Api.Data.SecurityAuditEvent StageEvent(string eventType, string? outcome, string? userId, int? companyId, string? ipAddress, string? userAgent, object? metadata = null)
        {
            Events.Add((eventType, outcome, userId, companyId, metadata));
            return new Api.Data.SecurityAuditEvent
            {
                EventType = eventType,
                Outcome = outcome,
                UserId = userId,
                CompanyId = companyId,
                IpAddress = ipAddress,
                UserAgent = userAgent
            };
        }
    }

    private sealed class ThrowingSecurityAuditService : ISecurityAuditService
    {
        public Task LogEventAsync(string eventType, string? userId = null, int? companyId = null, string? ipAddress = null, string? userAgent = null, object? details = null)
            => throw new InvalidOperationException("Audit store unavailable");

        public Task LogEventAsync(string eventType, string? outcome, string? userId, int? companyId, object? details)
            => throw new InvalidOperationException("Audit store unavailable");

        public Api.Data.SecurityAuditEvent StageEvent(string eventType, string? outcome, string? userId, int? companyId, string? ipAddress, string? userAgent, object? metadata = null)
            => throw new InvalidOperationException("Audit store unavailable");
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
