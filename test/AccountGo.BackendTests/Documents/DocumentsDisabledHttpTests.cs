// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Documents;

/// <summary>With <c>Documents:Enabled=false</c> every library route refuses, capabilities says so, and scan routes keep working.</summary>
public sealed class DocumentsDisabledHttpTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"accountgo_documents_off_{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _token = string.Empty;
    private string _slug = string.Empty;
    private string _documentId = string.Empty;
    private string _billId = string.Empty;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={_databasePath}");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("Documents:Enabled", "false");
        });
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var owner = new ApplicationUser { UserName = $"off-{Guid.NewGuid():N}@example.test", Email = $"off-{Guid.NewGuid():N}@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(owner)).Succeeded);
        _token = (await auth.CreateToken(owner, populateExp: true)).AccessToken;
        _slug = $"off-{Guid.NewGuid():N}"[..20];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organizations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = JsonContent.Create(new { name = "Documents Off", slug = _slug });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var companyId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.ResolveCompanyId(_factory.Services, created.RootElement.GetProperty("id").GetString()!);

        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var member = await db.CompanyMembers.SingleAsync(m => m.CompanyId == companyId && m.UserId == owner.Id);
        var fullAccess = await db.SecurityRoles.SingleAsync(r => r.Name == "WorkspaceFullAccess.v1");
        if (!await db.CompanyMemberSecurityRoles.AnyAsync(r => r.CompanyMemberId == member.Id && r.SecurityRoleId == fullAccess.Id))
            db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = fullAccess.Id });
        var bill = new Core.Domain.Purchases.PurchaseInvoiceHeader
        {
            CompanyId = companyId, PublicId = PublicIdGenerator.New("bill"), Date = DateTime.UtcNow.Date, VendorInvoiceNo = "OFF-1",
            Status = PurchaseInvoiceStatus.Draft
        };
        db.PurchaseInvoiceHeaders.Add(bill);
        var now = DateTime.UtcNow;
        var document = new Core.Domain.DocumentCapture
        {
            CompanyId = companyId, Kind = "document", SchemaVersion = 1, Status = "stored", SourceHash = new string('a', 64),
            ContentHash = new string('a', 64), NormalizationVersion = "pdf-structure-v1", ContentType = "application/pdf", ByteCount = 3,
            PageCount = 1, IdempotencyKey = $"seed-{Guid.NewGuid():N}", RequestFingerprint = new string('b', 64), CreatedBy = owner.Id,
            CreatedAt = now, ExpiresAt = now, FiledAt = now, FiledBy = owner.Id, Title = "Seeded",
            Blob = new DocumentCaptureBlob { CompanyId = companyId, DocumentBytes = [1, 2, 3] }
        };
        db.DocumentCaptures.Add(document);
        await db.SaveChangesAsync();
        _documentId = document.PublicId;
        _billId = bill.PublicId;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
            if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EveryLibraryRouteRefuses_CapabilitiesReportsDisabled_ScanRoutesStillWork()
    {
        using (var caps = Request(HttpMethod.Get, "/api/documents/capabilities"))
        using (var response = await _client.SendAsync(caps))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("enabled").GetBoolean());
        }

        var routes = new List<(HttpMethod Method, string Path, HttpContent? Content)>
        {
            (HttpMethod.Post, "/api/documents", Upload()),
            (HttpMethod.Get, "/api/documents", null),
            (HttpMethod.Get, $"/api/documents/linked/bill/{_billId}", null),
            (HttpMethod.Get, $"/api/documents/{_documentId}", null),
            (HttpMethod.Get, $"/api/documents/{_documentId}/bytes", null),
            (HttpMethod.Patch, $"/api/documents/{_documentId}", JsonContent.Create(new { title = "x" })),
            (HttpMethod.Post, $"/api/documents/{_documentId}/links", JsonContent.Create(new { targetType = "bill", targetId = _billId })),
            (HttpMethod.Delete, $"/api/documents/{_documentId}/links/bill/{_billId}", null),
            (HttpMethod.Post, $"/api/documents/{_documentId}/archive", null),
            (HttpMethod.Post, $"/api/documents/{_documentId}/unarchive", null),
            (HttpMethod.Delete, $"/api/documents/{_documentId}", null),
            (HttpMethod.Post, $"/api/documents/{_documentId}/restore", null),
            (HttpMethod.Delete, $"/api/documents/{_documentId}/permanent", null),
        };
        foreach (var (method, path, content) in routes)
        {
            using var request = Request(method, path);
            request.Content = content;
            using var response = await _client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path} returned {(int)response.StatusCode}");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("DOCUMENTS_DISABLED", body.RootElement.GetProperty("code").GetString());
        }

        using (var legacy = Request(HttpMethod.Get, $"/api/document-captures/linked/bill/{_billId}"))
        using (var response = await _client.SendAsync(legacy))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static MultipartFormDataContent Upload()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4\n%%EOF"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "File", "a.pdf");
        return form;
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Add("X-AccountGo-Org", _slug);
        return request;
    }
}
