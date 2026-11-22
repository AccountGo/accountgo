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
using Core.Domain.Purchases;
using Core.Domain.Security;
using Core.Entitlements;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureControllerHttpTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"accountgo_capture_http_{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _token = string.Empty;
    private string _companySlug = string.Empty;
    private int _companyId;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={_databasePath}");
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("DocumentCapture:Enabled", "true");
            builder.UseSetting("DocumentCapture:Extractor", "Fake");
            builder.UseSetting("DocumentCapture:FakeFixtures:purchase-invoice", "{\"date\":\"2026-11-20\",\"vendorName\":\"Fixture Vendor\"}");
        });
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = $"capture-{Guid.NewGuid():N}@example.test",
            Email = $"capture-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true
        };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        _token = (await auth.CreateToken(user, populateExp: true)).AccessToken;

        _companySlug = $"capture-{Guid.NewGuid():N}"[..20];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organizations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = JsonContent.Create(new { name = "Capture HTTP Test", slug = _companySlug });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        _companyId = AccountGo.BackendTests.PublicIds.PublicIdTestHelper.ResolveCompanyId(
            _factory.Services, created.RootElement.GetProperty("id").GetString()!);
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var company = await db.Companies.IgnoreQueryFilters().SingleAsync(item => item.Id == _companyId);
        company.Plan = PlanKeys.Community;
        company.DisabledModulesJson = "[]";
        var member = await db.CompanyMembers.SingleAsync(item => item.CompanyId == _companyId);
        foreach (var roleId in await db.SecurityRoles
                     .Where(role => role.Name == "Accountant" || role.Name == "Sales Representative" || role.Name == "Purchasing Agent")
                     .Select(role => role.Id).ToListAsync())
            db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole { CompanyMemberId = member.Id, SecurityRoleId = roleId });
        await db.SaveChangesAsync();
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
    public async Task RoutesUsePublicIdsAndRoundTripRealJsonThroughMvc()
    {
        var acceptLimit = System.Reflection.CustomAttributeData.GetCustomAttributes(
                typeof(Api.Controllers.DocumentCapturesController).GetMethod(nameof(Api.Controllers.DocumentCapturesController.Accept))!)
            .Single(attribute => attribute.AttributeType == typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute));
        Assert.Equal(256 * 1024, Convert.ToInt64(acceptLimit.ConstructorArguments[0].Value));
        using (var request = NewRequest(HttpMethod.Get, "/api/document-captures/capabilities"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var capabilities = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(capabilities.RootElement.GetProperty("enabled").GetBoolean());
            Assert.Contains("purchase-invoice", capabilities.RootElement.GetProperty("kinds").EnumerateArray()
                .Select(kind => kind.GetString()));
        }

        var uploaded = await UploadAsync(CreatePng(0xFF336699), "http-key-1");
        Assert.StartsWith("cap_", uploaded.GetProperty("id").GetString(), StringComparison.Ordinal);
        AssertJsonHasNoNumericWireIds(uploaded);
        Assert.Equal(JsonValueKind.Object, uploaded.GetProperty("fields").ValueKind);
        Assert.Equal("Fixture Vendor", uploaded.GetProperty("fields").GetProperty("vendorName").GetString());
        var publicId = uploaded.GetProperty("id").GetString()!;

        using (var request = NewRequest(HttpMethod.Get, $"/api/document-captures/{publicId}"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("fields").ValueKind);
            AssertJsonHasNoNumericWireIds(body.RootElement);
        }

        using (var request = NewRequest(HttpMethod.Get, $"/api/document-captures/{publicId}/bytes"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("no-store", response.Headers.CacheControl?.NoStore == true ? "no-store" : null);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        }

        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{publicId}/accept"))
        {
            request.Content = JsonContent.Create(new
            {
                fields = new { date = "2026-11-20", vendorName = "Reviewed Vendor", vendorInvoiceNo = "V-44" }
            });
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("accepted", accepted.RootElement.GetProperty("status").GetString());
            var fields = accepted.RootElement.GetProperty("acceptedFields");
            Assert.Equal(JsonValueKind.Object, fields.ValueKind);
            Assert.Equal("Reviewed Vendor", fields.GetProperty("vendorName").GetString());
            Assert.Equal("V-44", fields.GetProperty("vendorInvoiceNo").GetString());
            AssertJsonHasNoNumericWireIds(accepted.RootElement);
        }

        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{publicId}/accept"))
        {
            request.Content = JsonContent.Create(new { fields = new { date = "2026-11-20", vendorName = "Conflicting edit" } });
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var conflict = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("DOCUMENT_ACCEPT_CONFLICT", conflict.RootElement.GetProperty("code").GetString());
        }

        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{publicId}/link"))
        {
            request.Content = JsonContent.Create(new { targetType = "bill", targetId = "inv_wrong_prefix" });
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var invalid = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("invalid_id", invalid.RootElement.GetProperty("code").GetString());
        }


        var billId = await SeedBillAsync();
        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{publicId}/link"))
        {
            request.Content = JsonContent.Create(new { targetType = "bill", targetId = billId });
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var linked = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(billId, linked.RootElement.GetProperty("link").GetProperty("targetId").GetString());
        }

        using (var request = NewRequest(HttpMethod.Get, $"/api/document-captures/linked/bill/{billId}"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var linked = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, linked.RootElement.GetArrayLength());
            Assert.Equal(publicId, linked.RootElement[0].GetProperty("id").GetString());
        }

        var rejectedUpload = await UploadAsync(CreatePng(0xFFCC6633), "http-key-2");
        var rejectedId = rejectedUpload.GetProperty("id").GetString()!;
        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{rejectedId}/reject"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var rejected = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("rejected", rejected.RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task PublicIdErrorsAndOptionalIdempotencyKeyUseSharedWireContracts()
    {
        using (var request = NewRequest(HttpMethod.Get, "/api/document-captures/123"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("invalid_id", body.RootElement.GetProperty("code").GetString());
        }
        using (var request = NewRequest(HttpMethod.Get, "/api/document-captures/cap_00000000000000000000000000"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("unknown_id", body.RootElement.GetProperty("code").GetString());
        }

        var first = await UploadWithoutKeyAsync(CreatePng(0xFF225588));
        var second = await UploadWithoutKeyAsync(CreatePng(0xFF225588));
        Assert.Equal(first, second);
    }

    private async Task<JsonElement> UploadAsync(byte[] bytes, string idempotencyKey)
    {
        using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("purchase-invoice"), "Kind");
        form.Add(new StringContent("1"), "SchemaVersion");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "File", "invoice.png");
        request.Content = form;
        using var response = await _client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected upload success but received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<string> UploadWithoutKeyAsync(byte[] bytes)
    {
        using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("purchase-invoice"), "Kind");
        form.Add(new StringContent("1"), "SchemaVersion");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "File", "invoice.png");
        request.Content = form;
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> SeedBillAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        db.SuppressTenantSecurityCheck = true;
        var bill = new PurchaseInvoiceHeader
        {
            CompanyId = _companyId,
            PublicId = PublicIdGenerator.New("bill"),
            Date = new DateTime(2026, 11, 20),
            VendorInvoiceNo = "HTTP-BILL-1"
        };
        db.PurchaseInvoiceHeaders.Add(bill);
        await db.SaveChangesAsync();
        return bill.PublicId;
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Add("X-AccountGo-Org", _companySlug);
        return request;
    }

    private static byte[] CreatePng(uint color)
    {
        using var bitmap = new SkiaSharp.SKBitmap(8, 8);
        bitmap.Erase(new SkiaSharp.SKColor(color));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void AssertJsonHasNoNumericWireIds(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                    Assert.NotEqual(JsonValueKind.Number, property.Value.ValueKind);
                AssertJsonHasNoNumericWireIds(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) AssertJsonHasNoNumericWireIds(item);
        }
    }
}
