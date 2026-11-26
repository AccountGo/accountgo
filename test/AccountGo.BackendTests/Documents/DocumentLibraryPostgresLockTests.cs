// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Api.Data.Postgres;
using Api.DocumentCaptures;
using Api.PublicIds;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.Documents;

/// <summary>
/// Real Postgres interleavings for the documents lock order (design 3.8 R1, 3.9 S5, 3.11 U1): a bill that posts first always wins
/// against a concurrent unlink or trash, and a draft delete that holds the company lock first is seen by a waiting trash.
/// </summary>
public sealed class DocumentLibraryPostgresLockTests
{
    [PostgresOnlyFact]
    public async Task PostCommitsWhileUnlinkWaits_UnlinkSeesThePostedBillAndRefuses()
    {
        await using var fx = await Fixture.CreateAsync();
        var (docId, billA, _) = await fx.SeedLinkedDocumentAsync();

        await using var poster = fx.NewDb();
        await using var postTx = await poster.Database.BeginTransactionAsync();
        await poster.PurchaseInvoiceHeaders.Where(b => b.Id == billA.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, PurchaseInvoiceStatus.Open));

        await using var docDb = fx.NewDb();
        var unlink = fx.Service(docDb).UnlinkAsync(docId, "bill", billA.PublicId, CancellationToken.None);
        await Task.Delay(750);
        Assert.False(unlink.IsCompleted, "Unlink must wait for the posting transaction's row lock.");
        await postTx.CommitAsync();

        var refused = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => unlink.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("DOCUMENT_LOCKED_BY_POSTED_RECORD", refused.ErrorCode);
        await using var verify = fx.NewDb();
        Assert.Equal(2, await verify.DocumentCaptureLinks.CountAsync(l => l.CaptureId == docId));
    }

    [PostgresOnlyFact]
    public async Task TrashOfFileLinkedToTwoBills_WaitsForEitherPost_AndRefusesWhenOneIsPosted()
    {
        await using var fx = await Fixture.CreateAsync();
        var (docId, _, billB) = await fx.SeedLinkedDocumentAsync();

        await using var poster = fx.NewDb();
        await using var postTx = await poster.Database.BeginTransactionAsync();
        await poster.PurchaseInvoiceHeaders.Where(b => b.Id == billB.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, PurchaseInvoiceStatus.Open));

        await using var docDb = fx.NewDb();
        var trash = fx.Service(docDb).TrashAsync(docId, CancellationToken.None);
        await Task.Delay(750);
        Assert.False(trash.IsCompleted, "Trash must wait for every linked bill's row lock.");
        await postTx.CommitAsync();

        var refused = await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => trash.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("DOCUMENT_LOCKED_BY_POSTED_RECORD", refused.ErrorCode);
        await using var verify = fx.NewDb();
        Assert.Null((await verify.DocumentCaptures.SingleAsync(c => c.Id == docId)).DeletedAt);
    }

    [PostgresOnlyFact]
    public async Task DraftDeleteHoldingTheLocks_RemovesItsLinks_ThenAWaitingTrashSucceeds()
    {
        await using var fx = await Fixture.CreateAsync();
        var (docId, billA, billB) = await fx.SeedLinkedDocumentAsync();

        await using var deleter = fx.NewDb();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton<IDocumentCaptureAdmissionLock>(new DocumentCaptureAdmissionLock(deleter))
            .BuildServiceProvider();
        var deleteTx = await DocumentRecordDeletion.BeginAsync(services, deleter, fx.CompanyId, "bill", billB.Id, CancellationToken.None);
        Assert.NotNull(deleteTx);
        await DocumentLibraryService.RemoveLinksForDeletedTargetAsync(deleter, fx.CompanyId, "bill", billB.Id, CancellationToken.None);
        await deleter.PurchaseInvoiceHeaders.Where(b => b.Id == billB.Id).ExecuteDeleteAsync();

        // The trash can't get the company lock while the delete holds it: it reports busy rather than interleaving.
        await using var docDb = fx.NewDb();
        await Assert.ThrowsAsync<DocumentCaptureAdmissionBusyException>(() => fx.Service(docDb).TrashAsync(docId, CancellationToken.None));
        await deleteTx!.CommitAsync();
        await deleteTx.DisposeAsync();

        await using var retryDb = fx.NewDb();
        var trashed = await fx.Service(retryDb).TrashAsync(docId, CancellationToken.None);
        Assert.True(trashed.InTrash);
        Assert.Equal(billA.PublicId, Assert.Single(trashed.Links).TargetId);
    }

    [PostgresOnlyFact]
    public async Task ConcurrentUploadsAtTheRateLimit_OnlyOneIsStored()
    {
        await using var fx = await Fixture.CreateAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 3).Select(async i =>
        {
            await using var db = fx.NewDb();
            var builder = new PdfDocumentBuilder();
            builder.DocumentInformation.Title = $"race-{i}";
            builder.AddPage(100, 100);
            var pdf = builder.Build();
            await start.Task;
            try
            {
                await fx.Service(db, maxUploadsPerHour: 1).UploadAsync(pdf, "application/pdf", "a.pdf",
                    new DocumentMetadataInput(null, null, null, null), null, null, null, false, CancellationToken.None);
                return "stored";
            }
            catch (DocumentCaptureServiceException ex) { return ex.ErrorCode; }
            catch (DocumentCaptureAdmissionBusyException) { return "busy"; }
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(r => r == "stored"));
        Assert.All(results.Where(r => r != "stored"), r => Assert.Contains(r, new[] { "DOCUMENT_RATE_LIMITED", "busy" }));
        await using var verify = fx.NewDb();
        Assert.Equal(1, await verify.DocumentCaptures.CountAsync(c => c.Kind == "document"));
    }

    [PostgresOnlyFact]
    public async Task ConcurrentExportCreatesAndDownloads_OnlyEverHaveOneStreamAndFailCleanly()
    {
        await using var fx = await Fixture.CreateAsync();
        await fx.SeedLinkedDocumentAsync();
        string firstId;
        await using (var db = fx.NewDb())
            firstId = (await new DocumentExportService(db, new DocumentCaptureAdmissionLock(db), fx.TenantContext).CreateAsync(CancellationToken.None)).Id;
        int firstRunId;
        await using (var db = fx.NewDb())
            firstRunId = await db.DocumentExportRuns.Where(r => r.PublicId == firstId).Select(r => r.Id).SingleAsync();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 6).Select(async i =>
        {
            await using var db = fx.NewDb();
            var service = new DocumentExportService(db, new DocumentCaptureAdmissionLock(db), fx.TenantContext);
            await start.Task;
            try
            {
                if (i % 2 == 0)
                {
                    await service.CreateAsync(CancellationToken.None);
                    return "created";
                }
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                context.Response.Body = new MemoryStream();
                await service.StreamAsync(firstRunId, context.Response, "docs-lock-co", CancellationToken.None);
                return "streamed";
            }
            catch (DocumentCaptureServiceException ex) { return ex.ErrorCode; }
            catch (DocumentCaptureAdmissionBusyException) { return "busy"; }
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Contains(r, new[] { "created", "streamed", "DOCUMENT_EXPORT_RUNNING", "DOCUMENT_EXPORT_NOT_READY", "busy" }));
        Assert.True(results.Count(r => r == "streamed") <= 1);
        await using var verify = fx.NewDb();
        Assert.False(await verify.DocumentExportRuns.AnyAsync(r => r.Status == "running"));
    }

    [PostgresOnlyFact]
    public async Task Export_UnexpectedErrorMidStream_FailsTheRun_OnPostgres()
    {
        await using var fx = await Fixture.CreateAsync();
        await fx.SeedLinkedDocumentAsync();
        await using var db = fx.NewDb();
        var service = new DocumentExportService(db, new DocumentCaptureAdmissionLock(db), fx.TenantContext);
        var run = await service.CreateAsync(CancellationToken.None);
        var runId = await db.DocumentExportRuns.Where(r => r.PublicId == run.Id).Select(r => r.Id).SingleAsync();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new FailingStream();
        await service.StreamAsync(runId, context.Response, "docs-lock-co", CancellationToken.None);
        var after = await service.GetRunAsync(runId, CancellationToken.None);
        Assert.Equal(("failed", "server-error"), (after.Status, after.FailureReason));
        Assert.Equal("DOCUMENT_EXPORT_REQUIRED", await DocumentExportService.DeleteRefusalAsync(db, fx.CompanyId, true, false, CancellationToken.None));
    }

    [PostgresOnlyFact]
    public async Task Export_WorkspaceDeleteHoldingTheLock_ThenCreateAndClaimSeeTheDeletion_OnPostgres()
    {
        await using var fx = await Fixture.CreateAsync();
        await fx.SeedLinkedDocumentAsync();
        string readyId;
        await using (var db = fx.NewDb())
            readyId = (await new DocumentExportService(db, new DocumentCaptureAdmissionLock(db), fx.TenantContext).CreateAsync(CancellationToken.None)).Id;

        // Delete first: it holds the company lock while it commits, so a create waiting behind it sees a deleted workspace.
        await using (var deleter = fx.NewDb())
        {
            deleter.SuppressTenantSecurityCheck = true;
            await using var tx = await new DocumentCaptureAdmissionLock(deleter).AcquireCompanyAsync(fx.CompanyId, CancellationToken.None);
            await deleter.Companies.IgnoreQueryFilters().Where(c => c.Id == fx.CompanyId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, DateTime.UtcNow));
            await tx.CommitAsync();
        }
        await using var after = fx.NewDb();
        var service = new DocumentExportService(after, new DocumentCaptureAdmissionLock(after), fx.TenantContext);
        Assert.Equal("DOCUMENT_NOT_FOUND", (await Assert.ThrowsAsync<DocumentCaptureServiceException>(() => service.CreateAsync(CancellationToken.None))).ErrorCode);
        var runId = await after.DocumentExportRuns.Where(r => r.PublicId == readyId).Select(r => r.Id).SingleAsync();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        Assert.Equal("DOCUMENT_NOT_FOUND", (await Assert.ThrowsAsync<DocumentCaptureServiceException>(() =>
            service.StreamAsync(runId, context.Response, "docs-lock-co", CancellationToken.None))).ErrorCode);
    }

    [PostgresOnlyFact]
    public async Task Export_LeaseExpiredDuringStream_IsFencedAndANewExportCanStart_OnPostgres()
    {
        await using var fx = await Fixture.CreateAsync();
        await fx.SeedLinkedDocumentAsync();
        await using var db = fx.NewDb();
        var service = new DocumentExportService(db, new DocumentCaptureAdmissionLock(db), fx.TenantContext);
        var run = await service.CreateAsync(CancellationToken.None);
        var runId = await db.DocumentExportRuns.Where(r => r.PublicId == run.Id).Select(r => r.Id).SingleAsync();
        var gate = new GateStream();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = gate;
        var streaming = service.StreamAsync(runId, context.Response, "docs-lock-co", CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await using (var other = fx.NewDb())
        {
            await other.DocumentExportRuns.Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastHeartbeatAt, DateTime.UtcNow.AddMinutes(-6)));
            var next = await new DocumentExportService(other, new DocumentCaptureAdmissionLock(other), fx.TenantContext).CreateAsync(CancellationToken.None);
            Assert.Equal("ready", next.Status);
        }
        gate.Release.SetResult();
        await streaming.WaitAsync(TimeSpan.FromSeconds(30));
        await using var verify = fx.NewDb();
        var fenced = await verify.DocumentExportRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(("failed", "stale"), (fenced.Status, fenced.FailureReason));
        Assert.Equal(0, await verify.DocumentExportRuns.CountAsync(r => r.Status == "running"));
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new ArgumentOutOfRangeException(nameof(buffer), "Injected failure.");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new ArgumentOutOfRangeException(nameof(buffer), "Injected failure.");
    }

    private sealed class GateStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            await base.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        private readonly DbContextOptions<PostgresApiDbContext> _options;
        public Tenant TenantContext { get; }
        public int CompanyId { get; private set; }

        private Fixture(PostgresTestDatabase database)
        {
            _database = database;
            _options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(database.ConnectionString).Options;
            TenantContext = new Tenant();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fx = new Fixture(PostgresTestDatabase.CreateIfConfigured()!);
            await using var setup = new PostgresApiDbContext(fx._options, fx.TenantContext) { SuppressTenantSecurityCheck = true };
            await setup.Database.EnsureCreatedAsync();
            var company = new Company { Name = "Docs Lock Co", CompanyCode = "DLC", Slug = "docs-lock-co" };
            setup.Companies.Add(company);
            await setup.SaveChangesAsync();
            fx.CompanyId = company.Id;
            fx.TenantContext.CompanyIdValue = company.Id;
            return fx;
        }

        public PostgresApiDbContext NewDb() => new(_options, TenantContext);

        public DocumentLibraryService Service(ApiDbContext db, int maxUploadsPerHour = 120)
        {
            var entitlements = new Mock<IEntitlementService>();
            entitlements.Setup(e => e.GetDocumentStoragePolicy(It.IsAny<int>())).Returns(DocumentStoragePolicy.Community);
            entitlements.Setup(e => e.Get(It.IsAny<int>())).Returns(new EntitlementSnapshot(true, "community", "community", false, [], [], [], null));
            entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>())).Returns(new EntitlementDecision(true, null, null));
            var publicIds = new Mock<IPublicIdResolver>();
            publicIds.Setup(r => r.ResolveAsync(It.IsAny<string?>(), "bill", It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? publicId, string _, CancellationToken _) =>
                {
                    using var lookup = NewDb();
                    var id = lookup.PurchaseInvoiceHeaders.AsNoTracking().Where(b => b.PublicId == publicId).Select(b => (int?)b.Id).SingleOrDefault();
                    return id is > 0 ? PublicIdLookupResult.Success(id.Value) : PublicIdLookupResult.Fail(PublicIdLookupError.NotFound, "bill");
                });
            return new DocumentLibraryService(db, new DocumentCaptureAdmissionLock(db), entitlements.Object, publicIds.Object, TenantContext,
                Options.Create(new DocumentsOptions { MaxUploadsPerHour = maxUploadsPerHour }), Options.Create(new DocumentCaptureOptions()));
        }

        public async Task<(int DocumentId, PurchaseInvoiceHeader BillA, PurchaseInvoiceHeader BillB)> SeedLinkedDocumentAsync()
        {
            await using var db = NewDb();
            var billA = new PurchaseInvoiceHeader { CompanyId = CompanyId, Date = new DateTime(2026, 11, 20), VendorInvoiceNo = "A", Status = PurchaseInvoiceStatus.Draft };
            var billB = new PurchaseInvoiceHeader { CompanyId = CompanyId, Date = new DateTime(2026, 11, 20), VendorInvoiceNo = "B", Status = PurchaseInvoiceStatus.Draft };
            db.PurchaseInvoiceHeaders.AddRange(billA, billB);
            await db.SaveChangesAsync();
            var service = Service(db);
            var builder = new PdfDocumentBuilder();
            builder.AddPage(100, 100);
            var view = await service.UploadAsync(builder.Build(), "application/pdf", "contract.pdf", new DocumentMetadataInput(null, null, null, null),
                "bill", billA.PublicId, null, false, CancellationToken.None);
            var id = await db.DocumentCaptures.Where(c => c.PublicId == view.Id).Select(c => c.Id).SingleAsync();
            await service.LinkAsync(id, "bill", billB.PublicId, CancellationToken.None);
            return (id, billA, billB);
        }

        public ValueTask DisposeAsync()
        {
            _database.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Tenant : ITenantContext
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        public int CompanyIdValue { get; set; } = 1;
        public int? CompanyId => CompanyIdValue;
        public string? CompanySlug => "docs-lock-co";
        public CompanyMemberRole? Role => CompanyMemberRole.Member;
        public string? UserId => "actor-1";
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            SystemPermissions.DocumentsView, SystemPermissions.DocumentsManage,
            SystemPermissions.PurchasesBillsView, SystemPermissions.PurchasesBillsCreate
        };
        public bool IsResolved => true;
        public int RequireCompanyId() => CompanyIdValue;
        public IDisposable Impersonate(int id, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new Scope();
    }

    private sealed class PostgresOnlyFactAttribute : FactAttribute
    {
        public PostgresOnlyFactAttribute()
        {
            if (!PostgresTestDatabase.IsConfigured)
                Skip = $"Set {PostgresTestDatabase.EnvironmentVariable} to run PostgreSQL documents lock coverage.";
        }
    }
}
