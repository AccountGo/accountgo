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
