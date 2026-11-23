using Api.Data;
using Api.DocumentCaptures;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Xunit;
using CaptureEntity = Core.Domain.DocumentCapture;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureRetentionTests
{
    [Fact]
    public async Task Purge_DeletesOnlyAbandonedBytesAndExpiredAttempts()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new TestTenantContext(1);
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        await using var db = new ApiDbContext(options, tenant);
        await db.Database.EnsureCreatedAsync();
        var company = new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        tenant.SetCompanyId(company.Id);
        var now = DateTime.UtcNow;
        var accepted = Capture(company.Id, "cap_accepted", "accepted", now.AddDays(-30), now.AddDays(-30));
        var abandoned = Capture(company.Id, "cap_abandoned", "ready", now.AddDays(-8));
        var activeLease = Capture(company.Id, "cap_active", "extracting", now.AddDays(-8));
        activeLease.LeaseExpiresAt = now.AddMinutes(2);
        var expiredLease = Capture(company.Id, "cap_expiredlease", "extracting", now.AddDays(-8));
        expiredLease.LeaseExpiresAt = now.AddMinutes(-1);
        db.DocumentCaptures.AddRange(accepted, abandoned, activeLease, expiredLease);
        await db.SaveChangesAsync();
        foreach (var capture in new[] { accepted, abandoned, activeLease, expiredLease })
        {
            db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob
            {
                CompanyId = company.Id,
                CaptureId = capture.Id,
                DocumentBytes = [1, 2, 3]
            });
            db.DocumentCaptureAttempts.Add(new DocumentCaptureAttempt
            {
                CompanyId = company.Id,
                CaptureId = capture.Id,
                Generation = 1,
                ReservedAt = now.AddHours(-49)
            });
        }
        await db.SaveChangesAsync();

        var retention = new DocumentCaptureRetentionService(db, new DocumentCaptureAdmissionLock(db));
        await retention.PurgeAsync(now, CancellationToken.None);

        var blobs = await db.DocumentCaptureBlobs.IgnoreQueryFilters().Select(blob => blob.CaptureId).ToListAsync();
        Assert.Contains(accepted.Id, blobs);
        Assert.Contains(activeLease.Id, blobs);
        Assert.DoesNotContain(abandoned.Id, blobs);
        Assert.DoesNotContain(expiredLease.Id, blobs);
        Assert.Null((await db.DocumentCaptures.IgnoreQueryFilters().SingleAsync(capture => capture.Id == accepted.Id)).BytesDeletedAt);
        Assert.NotNull((await db.DocumentCaptures.IgnoreQueryFilters().SingleAsync(capture => capture.Id == abandoned.Id)).BytesDeletedAt);
        Assert.Empty(await db.DocumentCaptureAttempts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Purge_RemovesCaptureContentAndAttemptsForDeletedCompany()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new TestTenantContext(1);
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        await using var db = new ApiDbContext(options, tenant);
        await db.Database.EnsureCreatedAsync();
        var company = new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        tenant.SetCompanyId(company.Id);
        var capture = Capture(company.Id, "cap_deleted", "accepted", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1));
        capture.AcceptedFieldsJson = "{\"date\":\"2026-11-20\"}";
        capture.RawText = "sensitive data";
        db.DocumentCaptures.Add(capture);
        await db.SaveChangesAsync();
        db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob { CompanyId = company.Id, CaptureId = capture.Id, DocumentBytes = [7, 8, 9] });
        db.DocumentCaptureAttempts.Add(new DocumentCaptureAttempt { CompanyId = company.Id, CaptureId = capture.Id, Generation = 1, ReservedAt = DateTime.UtcNow });
        company.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var retention = new DocumentCaptureRetentionService(db, new DocumentCaptureAdmissionLock(db));
        var deleted = await retention.PurgeAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Empty(await db.DocumentCaptures.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.DocumentCaptureBlobs.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.DocumentCaptureAttempts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Purge_SkipsBusyCompanyAndContinuesWithOtherCompanyResidue()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new TestTenantContext(1);
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        await using var db = new ApiDbContext(options, tenant);
        await db.Database.EnsureCreatedAsync();
        var companyA = new Company { Name = "Busy Co", CompanyCode = "BUSY", Slug = "busy-co" };
        var companyB = new Company { Name = "Deleted Co", CompanyCode = "DEL", Slug = "deleted-co", DeletedAt = DateTime.UtcNow.AddDays(-1) };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();
        tenant.SetCompanyId(companyA.Id);
        var now = DateTime.UtcNow;
        var captureA = Capture(companyA.Id, "cap_busy", "ready", now.AddDays(-8));
        db.DocumentCaptures.Add(captureA);
        await db.SaveChangesAsync();
        tenant.SetCompanyId(companyB.Id);
        var captureB = Capture(companyB.Id, "cap_deleted_busy", "ready", now.AddDays(-8));
        db.DocumentCaptures.Add(captureB);
        await db.SaveChangesAsync();
        tenant.SetCompanyId(companyA.Id);
        db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob { CompanyId = companyA.Id, CaptureId = captureA.Id, DocumentBytes = [1, 2, 3] });
        db.DocumentCaptureAttempts.Add(new DocumentCaptureAttempt { CompanyId = companyA.Id, CaptureId = captureA.Id, Generation = 1, ReservedAt = now.AddHours(-49) });
        await db.SaveChangesAsync();
        tenant.SetCompanyId(companyB.Id);
        db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob { CompanyId = companyB.Id, CaptureId = captureB.Id, DocumentBytes = [4, 5, 6] });
        db.DocumentCaptureAttempts.Add(new DocumentCaptureAttempt { CompanyId = companyB.Id, CaptureId = captureB.Id, Generation = 1, ReservedAt = now.AddHours(-49) });
        await db.SaveChangesAsync();
        tenant.SetCompanyId(companyA.Id);

        var logger = new RetentionCapturingLogger();
        var retention = new DocumentCaptureRetentionService(db,
            new BusyCompanyAdmissionLock(db, companyA.Id), logger: logger);

        var deleted = await retention.PurgeAsync(now, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Contains(await db.DocumentCaptures.IgnoreQueryFilters().Select(item => item.Id).ToListAsync(), id => id == captureA.Id);
        Assert.NotNull(await db.DocumentCaptureBlobs.IgnoreQueryFilters().SingleOrDefaultAsync(blob => blob.CaptureId == captureA.Id));
        Assert.Single(await db.DocumentCaptureAttempts.IgnoreQueryFilters().Where(item => item.CompanyId == companyA.Id).ToListAsync());
        Assert.False(await db.DocumentCaptures.IgnoreQueryFilters().AnyAsync(item => item.CompanyId == companyB.Id));
        Assert.False(await db.DocumentCaptureBlobs.IgnoreQueryFilters().AnyAsync(item => item.CompanyId == companyB.Id));
        Assert.False(await db.DocumentCaptureAttempts.IgnoreQueryFilters().AnyAsync(item => item.CompanyId == companyB.Id));
        Assert.Single(logger.Messages);
        Assert.Contains("2 locked items", logger.Messages[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Purge_CatchesUpPreviouslyRejectedCapturesRegardlessOfExpiresAt_AndLeavesAcceptedUntouched()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new TestTenantContext(1);
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        await using var db = new ApiDbContext(options, tenant);
        await db.Database.EnsureCreatedAsync();
        var company = new Company { Name = "Capture Co", CompanyCode = "CAP", Slug = "capture-co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        tenant.SetCompanyId(company.Id);

        var now = DateTime.UtcNow;
        var rejectedBill = Capture(company.Id, "cap_rej_bill", "rejected", now.AddDays(5));
        rejectedBill.RejectedAt = now.AddMinutes(-5);
        rejectedBill.RejectedBy = "creator-1";
        rejectedBill.FieldsJson = "{\"vendorName\":\"Old Vendor\"}";
        rejectedBill.RawText = "old raw text";
        rejectedBill.WarningsJson = "[\"TOTAL_UNLABELED\"]";

        var rejectedAsst = Capture(company.Id, "cap_rej_asst", "rejected", now.AddDays(5));
        rejectedAsst.Kind = "assistant-attachment";
        rejectedAsst.RejectedAt = now.AddMinutes(-3);
        rejectedAsst.RejectedBy = "creator-1";
        rejectedAsst.FieldsJson = "{\"fields\":[{\"label\":\"Ref\",\"value\":\"123\"}]}";
        rejectedAsst.RawText = "assistant raw text";
        rejectedAsst.WarningsJson = "[\"REVIEW_REQUIRED\"]";

        var accepted = Capture(company.Id, "cap_acc_keep", "accepted", now.AddDays(-10), now.AddDays(-10));
        accepted.FieldsJson = "{\"vendorName\":\"Accepted Vendor\"}";
        accepted.RawText = "accepted raw text";

        db.DocumentCaptures.AddRange(rejectedBill, rejectedAsst, accepted);
        await db.SaveChangesAsync();
        foreach (var c in new[] { rejectedBill, rejectedAsst, accepted })
        {
            db.DocumentCaptureBlobs.Add(new DocumentCaptureBlob
            {
                CompanyId = company.Id,
                CaptureId = c.Id,
                DocumentBytes = [9, 8, 7]
            });
        }
        await db.SaveChangesAsync();

        var retention = new DocumentCaptureRetentionService(db, new DocumentCaptureAdmissionLock(db));
        await retention.PurgeAsync(now, CancellationToken.None);

        var blobs = await db.DocumentCaptureBlobs.IgnoreQueryFilters().Select(b => b.CaptureId).ToListAsync();
        Assert.DoesNotContain(rejectedBill.Id, blobs);
        Assert.DoesNotContain(rejectedAsst.Id, blobs);
        Assert.Contains(accepted.Id, blobs);

        var billAfter = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == rejectedBill.Id);
        Assert.NotNull(billAfter.BytesDeletedAt);
        Assert.NotNull(billAfter.FieldsWipedAt);
        Assert.Equal("{}", billAfter.FieldsJson);
        Assert.Null(billAfter.RawText);
        Assert.Null(billAfter.WarningsJson);

        var asstAfter = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == rejectedAsst.Id);
        Assert.NotNull(asstAfter.BytesDeletedAt);
        Assert.NotNull(asstAfter.FieldsWipedAt);
        Assert.Equal("{\"fields\":[]}", asstAfter.FieldsJson);
        Assert.Null(asstAfter.RawText);
        Assert.Null(asstAfter.WarningsJson);

        var accAfter = await db.DocumentCaptures.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == accepted.Id);
        Assert.Null(accAfter.BytesDeletedAt);
        Assert.Null(accAfter.FieldsWipedAt);
        Assert.Equal("{\"date\":\"2026-11-20\"}", accAfter.AcceptedFieldsJson);
    }

    private static CaptureEntity Capture(int companyId, string publicId, string status, DateTime expiresAt, DateTime? acceptedAt = null) => new()
    {
        CompanyId = companyId,
        PublicId = publicId,
        Kind = "purchase-invoice",
        SchemaVersion = 1,
        Status = status,
        SourceHash = new string('a', 64),
        ContentHash = new string('b', 64),
        NormalizationVersion = "image-normalization-v2",
        ContentType = "image/png",
        ByteCount = 3,
        PageCount = 1,
        RequestFingerprint = new string('c', 64),
        ExtractionGeneration = 1,
        CreatedBy = "creator-1",
        CreatedAt = expiresAt,
        ExpiresAt = expiresAt,
        AcceptedAt = acceptedAt,
        AcceptedFieldsJson = acceptedAt is null ? null : "{\"date\":\"2026-11-20\"}"
    };

    private sealed class TestTenantContext(int initialCompanyId) : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        private int _companyId = initialCompanyId;
        public int? CompanyId => _companyId;
        public string? CompanySlug => "capture-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public string? UserId => "creator-1";
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
        public bool IsResolved => true;
        public int RequireCompanyId() => _companyId;
        public void SetCompanyId(int value) => _companyId = value;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }

    private sealed class BusyCompanyAdmissionLock(ApiDbContext db, int busyCompanyId) : IDocumentCaptureAdmissionLock
    {
        private readonly DocumentCaptureAdmissionLock _inner = new(db);

        public Task<IDbContextTransaction> AcquireCompanyAsync(int companyId, CancellationToken cancellationToken) =>
            companyId == busyCompanyId
                ? Task.FromException<IDbContextTransaction>(new DocumentCaptureAdmissionBusyException())
                : _inner.AcquireCompanyAsync(companyId, cancellationToken);

        public Task<IDbContextTransaction> AcquirePurgeCompanyAsync(int companyId, CancellationToken cancellationToken) =>
            _inner.AcquirePurgeCompanyAsync(companyId, cancellationToken);

        public Task<IAsyncDisposable?> TryAcquirePurgeWorkerSingletonAsync(CancellationToken cancellationToken) =>
            _inner.TryAcquirePurgeWorkerSingletonAsync(cancellationToken);
    }

    private sealed class RetentionCapturingLogger : ILogger<DocumentCaptureRetentionService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
