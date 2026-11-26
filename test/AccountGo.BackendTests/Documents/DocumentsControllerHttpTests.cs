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
using Core.Entitlements;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.Documents;

public sealed class DocumentsControllerHttpTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"accountgo_documents_http_{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _ownerToken = string.Empty;
    private string _memberToken = string.Empty;
    private string _companySlug = string.Empty;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={_databasePath}");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        });
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var owner = new ApplicationUser { UserName = $"docs-{Guid.NewGuid():N}@example.test", Email = $"docs-{Guid.NewGuid():N}@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(owner)).Succeeded);
        _ownerToken = (await auth.CreateToken(owner, populateExp: true)).AccessToken;

        _companySlug = $"docs-{Guid.NewGuid():N}"[..20];
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/organizations"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ownerToken);
            request.Content = JsonContent.Create(new { name = "Documents HTTP Test", slug = _companySlug });
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var companyId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.ResolveCompanyId(
                _factory.Services, created.RootElement.GetProperty("id").GetString()!);

            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var ownerMember = await db.CompanyMembers.SingleAsync(m => m.CompanyId == companyId && m.UserId == owner.Id);
            var fullAccess = await db.SecurityRoles.SingleAsync(r => r.Name == "WorkspaceFullAccess.v1");
            if (!await db.CompanyMemberSecurityRoles.AnyAsync(r => r.CompanyMemberId == ownerMember.Id && r.SecurityRoleId == fullAccess.Id))
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = ownerMember.Id, SecurityRoleId = fullAccess.Id });

            var member = new ApplicationUser { UserName = $"docs-m-{Guid.NewGuid():N}@example.test", Email = $"docs-m-{Guid.NewGuid():N}@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(member)).Succeeded);
            _memberToken = (await auth.CreateToken(member, populateExp: true)).AccessToken;
            db.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = companyId, UserId = member.Id, Role = CompanyMemberRole.Member,
                Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
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
    public async Task FullAccessRoleCarriesDocumentPermissions_AndTheApiRoundTripsOverHttp()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var fullAccessPermissions = await db.SecurityRoles.Where(r => r.Name == "WorkspaceFullAccess.v1")
            .SelectMany(r => r.Permissions.Select(p => p.SecurityPermission.Name)).ToListAsync();
        Assert.Contains(SystemPermissions.DocumentsView, fullAccessPermissions);
        Assert.Contains(SystemPermissions.DocumentsManage, fullAccessPermissions);
        foreach (var other in new[] { "Accountant", "Auditor / Viewer", "Sales Representative", "Purchasing Agent", "GeneralUsers" })
        {
            var permissions = await db.SecurityRoles.Where(r => r.Name == other)
                .SelectMany(r => r.Permissions.Select(p => p.SecurityPermission.Name)).ToListAsync();
            Assert.DoesNotContain(SystemPermissions.DocumentsView, permissions);
            Assert.DoesNotContain(SystemPermissions.DocumentsManage, permissions);
        }

        // Upload, list, download.
        string id;
        using (var upload = Request(HttpMethod.Post, "/api/documents", _ownerToken))
        {
            upload.Headers.Add("Idempotency-Key", "http-up-1");
            upload.Content = UploadForm(CreatePdf(2), "application/pdf", "Lease.pdf", ("Category", "contract"), ("Title", "Office lease"));
            using var response = await _client.SendAsync(upload);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = body.RootElement.GetProperty("id").GetString()!;
            Assert.StartsWith("cap_", id, StringComparison.Ordinal);
            Assert.Equal("Office lease", body.RootElement.GetProperty("title").GetString());
            Assert.Equal("contract", body.RootElement.GetProperty("category").GetString());
        }

        using (var list = Request(HttpMethod.Get, "/api/documents?category=contract", _ownerToken))
        using (var response = await _client.SendAsync(list))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }

        using (var bytes = Request(HttpMethod.Get, $"/api/documents/{id}/bytes", _ownerToken))
        using (var response = await _client.SendAsync(bytes))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType ?? HeaderValue(response, "Content-Disposition")?.Split(';')[0]);
            Assert.Equal("nosniff", HeaderValue(response, "X-Content-Type-Options"));
            Assert.Equal("sandbox", HeaderValue(response, "Content-Security-Policy"));
            Assert.Contains("no-store", HeaderValue(response, "Cache-Control"));
        }

        using (var patch = Request(HttpMethod.Patch, $"/api/documents/{id}", _ownerToken))
        {
            patch.Content = JsonContent.Create(new { category = "secret" });
            using var response = await _client.SendAsync(patch);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("DOCUMENT_CATEGORY_INVALID", await CodeOf(response));
        }

        // A member without document permissions can't list or see the file, and gets the shared unknown-id shape.
        using (var list = Request(HttpMethod.Get, "/api/documents", _memberToken))
        using (var response = await _client.SendAsync(list))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var get = Request(HttpMethod.Get, $"/api/documents/{id}", _memberToken))
        using (var response = await _client.SendAsync(get))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var upload = Request(HttpMethod.Post, "/api/documents", _memberToken))
        {
            upload.Content = UploadForm(CreatePdf(1), "application/pdf", "a.pdf");
            using var response = await _client.SendAsync(upload);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("DOCUMENT_PERMISSION_REQUIRED", await CodeOf(response));
        }
    }

    [Fact]
    public async Task DeletingADraftBillRemovesItsLinksButKeepsTheFiles()
    {
        string billPublicId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var companyId = await db.Companies.IgnoreQueryFilters().Where(c => c.Slug == _companySlug).Select(c => c.Id).SingleAsync();
            var bill = new Core.Domain.Purchases.PurchaseInvoiceHeader
            {
                CompanyId = companyId, PublicId = Core.Domain.PublicIdGenerator.New("bill"), Date = DateTime.UtcNow.Date,
                VendorInvoiceNo = "HTTP-DOC-BILL", Status = Core.Domain.PurchaseInvoiceStatus.Draft
            };
            db.PurchaseInvoiceHeaders.Add(bill);
            await db.SaveChangesAsync();
            billPublicId = bill.PublicId;
        }

        string id;
        using (var upload = Request(HttpMethod.Post, "/api/documents", _ownerToken))
        {
            upload.Content = UploadForm(CreatePdf(1), "application/pdf", "receipt.pdf", ("LinkTargetType", "bill"), ("LinkTargetId", billPublicId));
            using var response = await _client.SendAsync(upload);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = body.RootElement.GetProperty("id").GetString()!;
            Assert.Equal(1, body.RootElement.GetProperty("links").GetArrayLength());
        }

        using (var delete = Request(HttpMethod.Delete, $"/api/Purchasing/DeleteBill?id={billPublicId}", _ownerToken))
        using (var response = await _client.SendAsync(delete))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var get = Request(HttpMethod.Get, $"/api/documents/{id}", _ownerToken))
        using (var response = await _client.SendAsync(get))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(0, body.RootElement.GetProperty("links").GetArrayLength());
        }
    }

    [Fact]
    public async Task OversizedUploadIsRefusedWith413AndTheLimit()
    {
        using var upload = Request(HttpMethod.Post, "/api/documents", _ownerToken);
        var tooBig = new byte[15 * 1024 * 1024 + 1];
        "%PDF-"u8.CopyTo(tooBig);
        upload.Content = UploadForm(tooBig, "application/pdf", "big.pdf");
        using var response = await _client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-AccountGo-Org", _companySlug);
        return request;
    }

    private static MultipartFormDataContent UploadForm(byte[] bytes, string contentType, string fileName, params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
            form.Add(new StringContent(value), name);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        return form;
    }

    private static string? HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues) : null;

    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }

    private static byte[] CreatePdf(int pages)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++)
            builder.AddPage(100, 100);
        return builder.Build();
    }
}
