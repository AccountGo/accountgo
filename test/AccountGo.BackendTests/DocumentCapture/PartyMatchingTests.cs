// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Api.Auditing;
using Api.Controllers;
using Api.Data;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Matching;
using Core.Domain;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class PartyMatchingTests(ITestOutputHelper output)
{
    private sealed class FixedTenantContext(int companyId, IEnumerable<string> permissions) : ITenantContext
    {
        public int? CompanyId => companyId;
        public string? CompanySlug => $"org-{companyId}";
        public bool IsResolved => true;
        public string? UserId => "user-test";
        public string? UserName => "tester@example.test";
        public CompanyMemberRole? Role => CompanyMemberRole.Member;
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(permissions, StringComparer.Ordinal);
        public int RequireCompanyId() => companyId;
        public IDisposable Impersonate(int targetCompanyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? targetPermissions = null) =>
            new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }

    private static IRequestAuditContext CreateAuditContext()
    {
        var mock = new Mock<IRequestAuditContext>();
        mock.SetupGet(a => a.ActorUserId).Returns("user-test");
        mock.SetupGet(a => a.ActorKind).Returns("user");
        mock.SetupGet(a => a.CorrelationId).Returns("corr-test");
        mock.SetupGet(a => a.IpAddress).Returns("127.0.0.1");
        return mock.Object;
    }

    private static ApiDbContext CreateTenantDb(SqliteConnection conn, int companyId, IEnumerable<string>? permissions = null)
    {
        var tenant = new FixedTenantContext(companyId, permissions ?? [SystemPermissions.PurchasesVendorsView, SystemPermissions.SalesCustomersView]);
        var interceptor = new RecordChangeInterceptor(CreateAuditContext());
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(interceptor)
            .Options;
        return new ApiDbContext(options, tenant);
    }

    private static Company SeedCompany(ApiDbContext db, string name, string code)
    {
        var c = new Company { Name = name, CompanyCode = code, ShortName = code, PublicId = $"ws_{Guid.NewGuid():N}"[..24] };
        db.Companies.Add(c);
        db.SaveChanges();
        return c;
    }

    private static Vendor SeedVendor(ApiDbContext db, int companyId, string name, string? email = null, string? phone = null, DateTime? archivedAt = null)
    {
        var party = new Party
        {
            CompanyId = companyId,
            PublicId = $"pty_{Guid.NewGuid():N}"[..24],
            PartyType = PartyTypes.Vendor,
            Name = name,
            Email = email,
            Phone = phone,
            IsActive = true,
        };
        var vendor = new Vendor
        {
            CompanyId = companyId,
            PublicId = $"ven_{Guid.NewGuid():N}"[..24],
            Party = party,
            ArchivedAt = archivedAt,
        };
        db.Vendors.Add(vendor);
        db.SaveChanges();
        return vendor;
    }

    private static Customer SeedCustomer(ApiDbContext db, int companyId, string name, string? email = null, string? phone = null, DateTime? archivedAt = null)
    {
        var party = new Party
        {
            CompanyId = companyId,
            PublicId = $"pty_{Guid.NewGuid():N}"[..24],
            PartyType = PartyTypes.Customer,
            Name = name,
            Email = email,
            Phone = phone,
            IsActive = true,
        };
        var customer = new Customer
        {
            CompanyId = companyId,
            PublicId = $"cus_{Guid.NewGuid():N}"[..24],
            Party = party,
            ArchivedAt = archivedAt,
        };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer;
    }

    private static Core.Domain.DocumentCapture SeedCapture(
        ApiDbContext db,
        int companyId,
        string kind,
        string fieldsJson,
        string? rawText = null)
    {
        var capture = new Core.Domain.DocumentCapture
        {
            CompanyId = companyId,
            PublicId = $"cap_{Guid.NewGuid():N}"[..24],
            Kind = kind,
            SchemaVersion = 1,
            Status = "ready",
            SourceHash = new string('1', 64),
            ContentHash = new string('2', 64),
            NormalizationVersion = "v1",
            ContentType = "image/png",
            ByteCount = 100,
            PageCount = 1,
            RequestFingerprint = new string('3', 64),
            FieldsJson = fieldsJson,
            RawText = rawText,
            CreatedBy = "tester@example.test",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
        };
        db.DocumentCaptures.Add(capture);
        db.SaveChanges();
        return capture;
    }

    [Theory]
    [Trait("Rule", "M-01")]
    [InlineData("Northwind Office Ltd.", "northwind office")]
    [InlineData("Northwind Office Limited", "northwind office")]
    [InlineData("Blue Harbor Retail, L.L.C.", "blue harbor retail")]
    [InlineData("Café Müller GmbH", "cafe muller")]
    [InlineData("Société Générale S.A.", "societe generale")]
    [InlineData("Apex Cloud Pte. Ltd.", "apex cloud")]
    [InlineData("Klang Valley Logistics Sdn. Bhd.", "klang valley logistics")]
    [InlineData("Harbor Holdings Pty Ltd", "harbor holdings")]
    [InlineData("Delta Systems B.V.", "delta systems")]
    [InlineData("Summit Partners LLP", "summit partners")]
    [InlineData("Oakridge Ventures PLC", "oakridge ventures")]
    [InlineData("Orion Corp.", "orion")]
    [InlineData("Meridian Incorporated", "meridian")]
    [InlineData("Cedar & Pine Co., Ltd.", "cedar pine")]
    [InlineData("บริษัท สยาม เทค จำกัด (มหาชน)", "สยาม เทค")]
    [InlineData("บริษัท สยามเทค จำกัด", "สยามเทค")]
    [InlineData("Limited", "limited")] // core-empty guard
    [InlineData("Co., Ltd.", "co ltd")] // core-empty guard
    [InlineData("บริษัท จำกัด", "บริษัท จำกัด")] // core-empty guard
    public void Normalizer_M01_HandlesAccentsPunctuationLegalSuffixesThaiAndCoreEmptyGuard(string input, string expected)
    {
        Assert.Equal(expected, PartyNameNormalizer.Normalize(input));
    }

    [Fact]
    [Trait("Rule", "M-02")]
    public async Task Scorer_M02_ScoresAndBreaksTiesByScoreRecencyAndName()
    {
        Assert.Equal(1.0, PartyNameMatcher.Score("Northwind Office Ltd", "Northwind Office Limited"));
        Assert.True(PartyNameMatcher.Score("Blue Harbor Retail", "Blue Harbour Retail") >= PartyNameMatcher.MatchThreshold);
        Assert.True(PartyNameMatcher.Score("Acme Supplies", "Acme Consulting") < PartyNameMatcher.MatchThreshold);

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var setupDb = CreateTenantDb(conn, 1);
        setupDb.Database.EnsureCreated();
        var company = SeedCompany(setupDb, "Co1", "CO1");

        using var db = CreateTenantDb(conn, company.Id);
        var vOlder = SeedVendor(db, company.Id, "Northwind Office Ltd");
        var vNewer = SeedVendor(db, company.Id, "Northwind Office Inc");
        var vAlpha = SeedVendor(db, company.Id, "Northwind Office Corp");
        var vFourth = SeedVendor(db, company.Id, "Northwind Office LLC");

        // Give vNewer a more recent bill than vOlder; vAlpha and vFourth have no bills (tie broken by name).
        db.PurchaseInvoiceHeaders.AddRange(
            new PurchaseInvoiceHeader { CompanyId = company.Id, VendorId = vOlder.Id, Date = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), PublicId = $"bil_{Guid.NewGuid():N}"[..24] },
            new PurchaseInvoiceHeader { CompanyId = company.Id, VendorId = vNewer.Id, Date = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc), PublicId = $"bil_{Guid.NewGuid():N}"[..24] });
        db.SaveChanges();

        var capture = SeedCapture(db, company.Id, "purchase-invoice", "{\"vendorName\":\"Northwind Office\"}");
        var matches = await PartyMatchingService.MatchCaptureAsync(db, "vendor", capture.Id, CancellationToken.None);

        Assert.NotNull(matches);
        Assert.Equal(3, matches!.Count); // top 3 cap
        Assert.Equal(vNewer.PublicId, matches[0].PartyId); // most recently used first
        Assert.Equal(vOlder.PublicId, matches[1].PartyId); // next most recently used
        Assert.Equal(vAlpha.PublicId, matches[2].PartyId); // alphabetical tie-break (Corp before LLC)
        Assert.All(matches, m => Assert.Equal("name", m.Reason));
    }

    [Fact]
    [Trait("Rule", "M-02")]
    public void MatchingAccuracy_M02_44InventedPairsMeetPrecisionAndRecall()
    {
        var pairs = new (string Extracted, string Stored, bool ExpectedMatch, string Category)[]
        {
            // Positive pairs (24): suffix, case, punctuation, accents, Thai, token order, minor OCR typos
            ("Northwind Office", "Northwind Office Limited", true, "suffix"),
            ("Northwind Office Ltd.", "NORTHWIND OFFICE CO., LTD.", true, "suffix+case"),
            ("Blue Harbor Retail", "Blue Harbor Retail LLC", true, "suffix"),
            ("Blue Harbor Retail", "Blue Harbor Retail, L.L.C.", true, "suffix+punct"),
            ("Apex Cloud Systems", "Apex Cloud Systems Pte. Ltd.", true, "suffix"),
            ("Klang Valley Logistics", "Klang Valley Logistics Sdn Bhd", true, "suffix"),
            ("Cafe Muller", "Café Müller GmbH", true, "accents+suffix"),
            ("Societe Lumiere", "Société Lumière S.A.", true, "accents+suffix"),
            ("Cedar and Pine", "Cedar & Pine Co.", true, "and-vs-amp"),
            ("Cedar & Pine", "Cedar Pine Co., Ltd.", true, "punct+suffix"),
            ("Harbor-View Hardware", "Harbor View Hardware Pty Ltd", true, "punct+suffix"),
            ("Meridian Tech", "Meridian Tech Incorporated", true, "suffix"),
            ("Summit Advisory", "Summit Advisory LLP", true, "suffix"),
            ("Oakridge Capital", "Oakridge Capital PLC", true, "suffix"),
            ("Delta Logistics", "Delta Logistics B.V.", true, "suffix"),
            ("สยาม เทค", "บริษัท สยาม เทค จำกัด", true, "thai+suffix"),
            ("สยาม เทค", "บริษัท สยาม เทค จำกัด (มหาชน)", true, "thai+pcl"),
            ("บางกอก ซัพพลาย", "บริษัท บางกอก ซัพพลาย จำกัด", true, "thai+suffix"),
            ("Blue Harbour Retail", "Blue Harbor Retail LLC", true, "spelling-variant"),
            ("Northwind Ofice", "Northwind Office Ltd", true, "ocr-1char"),
            ("Green Leaf Grocer", "Green Leaf Grocers", true, "plural"),
            ("Sunrise Bakry", "Sunrise Bakery", true, "ocr-1char"),
            ("Office Northwind", "Northwind Office Ltd", true, "token-order"),
            ("Orion Digital", "ORION DIGITAL CORP.", true, "case+suffix"),
            ("Silver Creek Coffee", "Silver Creek Coffee Company", true, "suffix"),

            // Negative pairs (20): near-miss distinct businesses
            ("Acme Supplies", "Acme Consulting", false, "near-miss"),
            ("North Star Logistics", "South Star Logistics", false, "near-miss"),
            ("Blue Harbor Retail", "Red Harbor Retail", false, "near-miss"),
            ("Green Leaf Grocers", "Green Valley Grocers", false, "near-miss"),
            ("Harbor Hardware", "Harbor Software", false, "near-miss"),
            ("Sunrise Bakery", "Sunset Bakery", false, "near-miss"),
            ("Apex Cloud", "Apex Printing", false, "near-miss"),
            ("Corner Cafe", "Corner Pharmacy", false, "near-miss"),
            ("Summit Partners", "Valley Partners", false, "near-miss"),
            ("Meridian Health", "Meridian Freight", false, "near-miss"),
            ("Cedar Pine", "Maple Oak", false, "distinct"),
            ("Silver Creek Coffee", "Gold Creek Bakery", false, "near-miss"),
            ("Delta Systems", "Alpha Systems", false, "near-miss"),
            ("First Metro Bank", "Second Metro Bank", false, "near-miss"),
            ("Pacific Trading", "Atlantic Trading", false, "near-miss"),
            ("Metro Office Supply", "Urban Office Furniture", false, "near-miss"),
            ("สยาม เทค", "สยาม ฟู้ด", false, "thai-near-miss"),
            ("บางกอก ซัพพลาย", "เชียงใหม่ ซัพพลาย", false, "thai-near-miss"),
            ("Northwind Office", "Northwind Catering", false, "near-miss"),
        };

        Assert.Equal(44, pairs.Length);

        int tp = 0, fp = 0, tn = 0, fn = 0;
        var misses = new List<string>();
        foreach (var (extracted, stored, expected, cat) in pairs)
        {
            var score = PartyNameMatcher.Score(extracted, stored);
            var predicted = score >= PartyNameMatcher.MatchThreshold;
            if (expected && predicted) tp++;
            else if (!expected && predicted) { fp++; misses.Add($"FP [{cat}] '{extracted}' vs '{stored}' = {score:0.0000}"); }
            else if (!expected && !predicted) tn++;
            else { fn++; misses.Add($"FN [{cat}] '{extracted}' vs '{stored}' = {score:0.0000}"); }
        }

        var precision = tp + fp > 0 ? (double)tp / (tp + fp) : 1.0;
        var recall = tp + fn > 0 ? (double)tp / (tp + fn) : 1.0;
        output.WriteLine($"Matching accuracy (44 pairs, threshold {PartyNameMatcher.MatchThreshold:0.00}): TP={tp}, FP={fp}, TN={tn}, FN={fn}, Precision={precision:P1}, Recall={recall:P1}");
        foreach (var miss in misses)
            output.WriteLine("  " + miss);

        Assert.True(precision >= 0.95, $"Precision {precision:P1} below 95%: {string.Join("; ", misses)}");
        Assert.True(recall >= 0.95, $"Recall {recall:P1} below 95%: {string.Join("; ", misses)}");
    }

    [Fact]
    [Trait("Rule", "M-03")]
    public async Task Identifiers_M03_MatchesEmailAndPhoneAboveNameAndIgnoresMaskedValues()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var setupDb = CreateTenantDb(conn, 1);
        setupDb.Database.EnsureCreated();
        var company = SeedCompany(setupDb, "IdCo", "IDCO");

        using var db = CreateTenantDb(conn, company.Id);
        var byEmail = SeedVendor(db, company.Id, "Unrelated Legal Entity Ltd", email: "billing@example-vendor.test");
        var byPhone = SeedVendor(db, company.Id, "Another Entity LLC", phone: "+1 (415) 555-0198");
        var byName = SeedVendor(db, company.Id, "Northwind Office Ltd");
        var maskedParty = SeedVendor(db, company.Id, "Masked Party Ltd", email: "••••@example.test", phone: "••••");

        // Email match ranks above exact name match
        var emailCapture = SeedCapture(
            db, company.Id, "purchase-invoice",
            "{\"vendorName\":\"Northwind Office\"}",
            rawText: "Northwind Office\nEmail: BILLING@EXAMPLE-VENDOR.TEST\nTotal: USD 50.00");
        var emailMatches = await PartyMatchingService.MatchCaptureAsync(db, "vendor", emailCapture.Id, CancellationToken.None);
        Assert.NotNull(emailMatches);
        Assert.Equal(2, emailMatches!.Count);
        Assert.Equal(byEmail.PublicId, emailMatches[0].PartyId);
        Assert.Equal("identifier", emailMatches[0].Reason);
        Assert.Equal(byName.PublicId, emailMatches[1].PartyId);
        Assert.Equal("name", emailMatches[1].Reason);

        // Phone last-8 digits match
        var phoneCapture = SeedCapture(
            db, company.Id, "purchase-invoice",
            "{\"vendorName\":\"Unknown Label\"}",
            rawText: "Tel: 001-415-555-0198\nTotal: USD 10.00");
        var phoneMatches = await PartyMatchingService.MatchCaptureAsync(db, "vendor", phoneCapture.Id, CancellationToken.None);
        Assert.NotNull(phoneMatches);
        var phoneHit = Assert.Single(phoneMatches!);
        Assert.Equal(byPhone.PublicId, phoneHit.PartyId);
        Assert.Equal("identifier", phoneHit.Reason);

        // Masked values ("••••") are never compared
        var maskedCapture = SeedCapture(
            db, company.Id, "purchase-invoice",
            "{\"vendorName\":\"••••\"}",
            rawText: "Email: ••••@example.test\nTel: ••••\nTotal: USD 10.00");
        var maskedMatches = await PartyMatchingService.MatchCaptureAsync(db, "vendor", maskedCapture.Id, CancellationToken.None);
        Assert.NotNull(maskedMatches);
        Assert.Empty(maskedMatches!);
        Assert.NotEqual(maskedParty.PublicId, emailMatches[0].PartyId);
    }

    [Fact]
    [Trait("Rule", "M-04")]
    public async Task Aliases_M04_CreatedOnSaveEnforcesUniqueConstraintTenantIsolationAndAuditsRemoval()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var setupDb = CreateTenantDb(conn, 1);
        setupDb.Database.EnsureCreated();
        var co1 = SeedCompany(setupDb, "TenantOne", "T1");
        var co2 = SeedCompany(setupDb, "TenantTwo", "T2");

        using var db1 = CreateTenantDb(conn, co1.Id);
        var vendor1 = SeedVendor(db1, co1.Id, "Northwind Office Limited");
        var capture1 = SeedCapture(db1, co1.Id, "purchase-invoice", "{\"vendorName\":\"NW Office Supply\"}");

        // Before save: no alias match
        var before = await PartyMatchingService.MatchCaptureAsync(db1, "vendor", capture1.Id, CancellationToken.None);
        Assert.NotNull(before);
        Assert.Empty(before!);

        // Record alias on save
        PartyMatchingService.RecordAliasesForCaptures(db1, co1.Id, "vendor", vendor1.PartyId!.Value, [capture1.Id], "tester@example.test");
        db1.SaveChanges();

        // Rescan: exact alias hit
        var after = await PartyMatchingService.MatchCaptureAsync(db1, "vendor", capture1.Id, CancellationToken.None);
        Assert.NotNull(after);
        var aliasHit = Assert.Single(after!);
        Assert.Equal(vendor1.PublicId, aliasHit.PartyId);
        Assert.Equal("alias", aliasHit.Reason);
        Assert.Equal(1.0, aliasHit.Score);

        // Unique constraint on (CompanyId, Role, NormalizedAlias)
        db1.PartyAliases.Add(new PartyAlias
        {
            CompanyId = co1.Id,
            PartyId = vendor1.PartyId.Value,
            Role = "vendor",
            NormalizedAlias = "nw office supply",
            CreatedBy = "tester@example.test",
            CreatedAt = DateTime.UtcNow,
        });
        Assert.Throws<DbUpdateException>(() => db1.SaveChanges());
        db1.ChangeTracker.Clear();

        // Tenant isolation: Tenant 2 cannot see Tenant 1's alias
        using var db2 = CreateTenantDb(conn, co2.Id);
        Assert.Empty(db2.PartyAliases.ToList());

        // Removal is audited by RecordChangeInterceptor
        var toDelete = db1.PartyAliases.Single(a => a.NormalizedAlias == "nw office supply");
        db1.PartyAliases.Remove(toDelete);
        db1.SaveChanges();

        var audit = db1.RecordChanges
            .OrderByDescending(r => r.Id)
            .FirstOrDefault(r => r.EntityType == "PartyAlias" && r.Action == "Deleted");
        Assert.NotNull(audit);
        Assert.Equal("Party", audit!.ParentEntityType);
        Assert.Equal(vendor1.PartyId.Value, audit.ParentEntityId);
    }

    [Fact]
    [Trait("Rule", "M-05")]
    public async Task Endpoint_M05_EnforcesRolePermissionsTenantIsolationAndTop3Cap()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var setupDb = CreateTenantDb(conn, 1);
        setupDb.Database.EnsureCreated();
        var co1 = SeedCompany(setupDb, "EndpointCo1", "EP1");
        var co2 = SeedCompany(setupDb, "EndpointCo2", "EP2");

        using var db1 = CreateTenantDb(conn, co1.Id);
        SeedVendor(db1, co1.Id, "Northwind Office Ltd");
        SeedVendor(db1, co1.Id, "Northwind Office Inc");
        SeedVendor(db1, co1.Id, "Northwind Office LLC");
        SeedVendor(db1, co1.Id, "Northwind Office Corp");
        var captureCo1 = SeedCapture(db1, co1.Id, "purchase-invoice", "{\"vendorName\":\"Northwind Office\"}");

        using var db2 = CreateTenantDb(conn, co2.Id);
        var captureCo2 = SeedCapture(db2, co2.Id, "purchase-invoice", "{\"vendorName\":\"Northwind Office\"}");

        DocumentCapturesController BuildController(ApiDbContext ctx, int companyId, params string[] perms)
        {
            var tenant = new FixedTenantContext(companyId, perms);
            var opts = Options.Create(new DocumentCaptureOptions());
            var extractor = new Mock<IDocumentFieldExtractor>();
            var entitlements = new Mock<IEntitlementService>();
            var controller = new DocumentCapturesController(null!, tenant, entitlements.Object, opts, extractor.Object, ctx)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            return controller;
        }

        // Missing vendor view permission -> 403
        var noPermController = BuildController(db1, co1.Id, SystemPermissions.SalesCustomersView);
        var deniedVendor = await noPermController.MatchParty("vendor", captureCo1.Id, CancellationToken.None);
        var deniedObj = Assert.IsType<ObjectResult>(deniedVendor);
        Assert.Equal(StatusCodes.Status403Forbidden, deniedObj.StatusCode);

        // Missing customer view permission -> 403
        var vendorOnlyController = BuildController(db1, co1.Id, SystemPermissions.PurchasesVendorsView);
        var deniedCustomer = await vendorOnlyController.MatchParty("customer", captureCo1.Id, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(deniedCustomer).StatusCode);

        // Another tenant's capture -> 404
        var crossTenant = await vendorOnlyController.MatchParty("vendor", captureCo2.Id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(crossTenant);

        // Invalid role -> 400
        var badRole = await vendorOnlyController.MatchParty("bank", captureCo1.Id, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badRole);

        // Authorized same-tenant -> 200 with at most 3 candidates (never full party list of 4)
        var okResult = Assert.IsType<OkObjectResult>(await vendorOnlyController.MatchParty("vendor", captureCo1.Id, CancellationToken.None));
        var candidates = Assert.IsAssignableFrom<IReadOnlyList<PartyMatchCandidate>>(okResult.Value);
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    [Trait("Rule", "M-06")]
    public async Task Archived_M06_ReturnedWithArchivedFlagAndNeverPreselected()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var setupDb = CreateTenantDb(conn, 1);
        setupDb.Database.EnsureCreated();
        var co = SeedCompany(setupDb, "ArchCo", "ARCH");

        using var db = CreateTenantDb(conn, co.Id);
        var archivedCustomer = SeedCustomer(db, co.Id, "Blue Harbor Retail LLC", email: "ap@blueharbor.test", archivedAt: DateTime.UtcNow.AddDays(-10));
        var capture = SeedCapture(
            db, co.Id, "sales-invoice",
            "{\"customerName\":\"Blue Harbor Retail\"}",
            rawText: "Bill To: Blue Harbor Retail\nap@blueharbor.test");

        var matches = await PartyMatchingService.MatchCaptureAsync(db, "customer", capture.Id, CancellationToken.None);
        Assert.NotNull(matches);
        var hit = Assert.Single(matches!);
        Assert.Equal(archivedCustomer.PublicId, hit.PartyId);
        Assert.Equal("identifier", hit.Reason);
        Assert.True(hit.Archived);
    }
}
