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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureControllerHttpTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"accountgo_capture_http_{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _token = string.Empty;
    private string _secondToken = string.Empty;
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
        var secondUser = new ApplicationUser
        {
            UserName = $"capture-second-{Guid.NewGuid():N}@example.test",
            Email = $"capture-second-{Guid.NewGuid():N}@example.test",
            EmailConfirmed = true
        };
        Assert.True((await users.CreateAsync(secondUser)).Succeeded);
        _secondToken = (await auth.CreateToken(secondUser, populateExp: true)).AccessToken;
        db.CompanyMembers.Add(new CompanyMember
        {
            CompanyId = _companyId, UserId = secondUser.Id, Role = CompanyMemberRole.Member,
            Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow
        });
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
    public async Task LocalOcrQueueRejectsFifthWaiterBeforeReservingQuotaAndRetriesImmediately()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Api.DocumentCaptures.CaptureWorkerResult([], [1], [], []),
            Api.DocumentCaptures.CaptureWorkerProtocol.Json);
        var firstWorker = new LocalOcrWorkerLimitsTests.FakeWorker(json);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerCount = 0;
        using var extractor = new Api.DocumentCaptures.LocalOcrDocumentFieldExtractor(
            Microsoft.Extensions.Options.Options.Create(new Api.DocumentCaptures.DocumentCaptureOptions()),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Api.DocumentCaptures.LocalOcrDocumentFieldExtractor>.Instance,
            _ =>
            {
                if (Interlocked.Increment(ref workerCount) == 1)
                {
                    started.TrySetResult();
                    return firstWorker;
                }
                var worker = new LocalOcrWorkerLimitsTests.FakeWorker(json);
                worker.Complete();
                return worker;
            });
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Api.DocumentCaptures.IDocumentFieldExtractor>();
            services.AddSingleton<Api.DocumentCaptures.IDocumentFieldExtractor>(extractor);
        }));
        using var client = factory.CreateClient();
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var seedResponse = await UploadAsync(6, _client);
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);
        using var seedBody = JsonDocument.Parse(await seedResponse.Content.ReadAsStringAsync());
        var expectedId = seedBody.RootElement.GetProperty("id").GetString();

        var first = UploadAsync(0);
        Task<HttpResponseMessage>[] queued = [];
        try
        {
            await started.Task.WaitAsync(watchdog.Token);
            queued = Enumerable.Range(1, 4).Select(number => UploadAsync(number)).ToArray();
            while (extractor.WaitingCount < 4)
                await Task.Delay(10, watchdog.Token);

            Assert.Equal((2, 2), await CountRowsAsync());
            foreach (var key in new[] { "ocr-queue-6", "ocr-queue-duplicate" })
            {
                using var replay = await UploadAsync(6, key: key).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                using var body = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
                Assert.Equal(expectedId, body.RootElement.GetProperty("id").GetString());
            }
            Assert.Equal((2, 2), await CountRowsAsync());
            using var rejected = await UploadAsync(5);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Equal("DOCUMENT_DECODE_BUSY", await ReadProblemCodeAsync(rejected));
            Assert.Equal((2, 2), await CountRowsAsync());
        }
        finally
        {
            firstWorker.Complete();
            foreach (var pending in queued.Prepend(first))
            {
                using var completed = await pending;
                Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            }
        }

        Assert.Equal((6, 6), await CountRowsAsync());
        using var retry = await UploadAsync(5);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((7, 7), await CountRowsAsync());

        async Task<HttpResponseMessage> UploadAsync(int number, HttpClient? uploadClient = null, string? key = null)
        {
            using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
            request.Headers.Add("Idempotency-Key", key ?? $"ocr-queue-{number}");
            request.Content = CreateUploadForm("purchase-invoice", CreatePng(0xFFCCDD00u + (uint)number), "image/png");
            return await (uploadClient ?? client).SendAsync(request, watchdog.Token);
        }

        async Task<(int Attempts, int Captures)> CountRowsAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var attempts = await db.DocumentCaptureAttempts.IgnoreQueryFilters().CountAsync(row => row.CompanyId == _companyId);
            var captures = await db.DocumentCaptures.IgnoreQueryFilters().CountAsync(row => row.CompanyId == _companyId);
            return (attempts, captures);
        }
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
            Assert.True(response.Headers.CacheControl?.NoStore == true);
            Assert.Contains("private", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task AssistantOwnerMismatchReturnsByteIdenticalUnknownIdAcrossRoutesAndDoesNotDedupe()
    {
        var source = CreatePng(0xFFAA5511);
        var ownerId = await UploadKindAsync(_token, "assistant-attachment", source, "assistant-shared-key");
        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{ownerId}/accept"))
        {
            request.Content = JsonContent.Create(new { fields = new { fields = new[] { new { label = "Total", value = "12" } } } });
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var missingRequest = NewRequest(HttpMethod.Get, "/api/document-captures/cap_00000000000000000000000000");
        using var missingResponse = await _client.SendAsync(missingRequest);
        var missingBody = await missingResponse.Content.ReadAsByteArrayAsync();

        foreach (var (method, path, content) in new (HttpMethod Method, string Path, HttpContent? Content)[]
        {
            (HttpMethod.Get, $"/api/document-captures/{ownerId}", null),
            (HttpMethod.Get, $"/api/document-captures/{ownerId}/bytes", null),
            (HttpMethod.Post, $"/api/document-captures/{ownerId}/accept", JsonContent.Create(new { fields = new { fields = new[] { new { label = "Total", value = "12" } } } })),
            (HttpMethod.Post, $"/api/document-captures/{ownerId}/reject", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            (HttpMethod.Post, $"/api/document-captures/{ownerId}/link", JsonContent.Create(new { targetType = "bill", targetId = "bill_missing" }))
        })
        {
            using var request = NewRequestForToken(method, path, _secondToken);
            request.Content = content;
            using var response = await _client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"Expected shared 404 for {method} {path}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(missingBody, await response.Content.ReadAsByteArrayAsync());
        }

        var otherCreatorId = await UploadKindAsync(_secondToken, "assistant-attachment", source, "assistant-shared-key");
        Assert.NotEqual(ownerId, otherCreatorId);
    }

    [Fact]
    public async Task UploadHttpMatrixCoversAuthenticationPermissionsMediaAndPdfBytePreservation()
    {
        using (var unauthenticated = new HttpRequestMessage(HttpMethod.Get, "/api/document-captures/capabilities"))
        using (var response = await _client.SendAsync(unauthenticated))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using (var request = NewRequestForToken(HttpMethod.Post, "/api/document-captures", _secondToken))
        using (var form = CreateUploadForm("purchase-invoice", CreatePng(0xFFABCD12), "image/png"))
        {
            request.Content = form;
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("DOCUMENT_PERMISSION_REQUIRED", problem.RootElement.GetProperty("code").GetString());
        }

        using (var request = NewRequest(HttpMethod.Post, "/api/document-captures"))
        using (var form = CreateUploadForm("purchase-invoice", Array.Empty<byte>(), "image/png"))
        {
            request.Content = form;
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("DOCUMENT_EMPTY", (await ReadProblemCodeAsync(response)));
        }

        using (var request = NewRequest(HttpMethod.Post, "/api/document-captures"))
        using (var form = CreateUploadForm("purchase-invoice", [0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "image/gif"))
        {
            request.Content = form;
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", await ReadProblemCodeAsync(response));
        }

        var oversized = new byte[Api.DocumentCaptures.DocumentCaptureImageLimits.MaxInputBytes + 1];
        using (var request = NewRequest(HttpMethod.Post, "/api/document-captures"))
        using (var form = CreateUploadForm("purchase-invoice", oversized, "image/png"))
        {
            request.Content = form;
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal("DOCUMENT_TOO_LARGE", await ReadProblemCodeAsync(response));
        }

        var pdfBuilder = new PdfDocumentBuilder();
        pdfBuilder.AddPage(100, 100);
        var pdf = pdfBuilder.Build();
        var pdfId = await UploadFileAsync(pdf, "application/pdf", "pdf-http-key");
        using (var request = NewRequest(HttpMethod.Get, $"/api/document-captures/{pdfId}/bytes"))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(pdf, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.True(response.Headers.CacheControl?.NoStore == true);
            Assert.Contains("private", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        }
    }

    [Fact]
    public async Task PdfParseDeadlineReturnsRetryAfterAndDocumentBusyOverHttp()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, ExpiringPdfClock>();
        }));
        using var client = factory.CreateClient();
        var pdf = new PdfDocumentBuilder();
        pdf.AddPage(100, 100);
        using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
        request.Content = CreateUploadForm("purchase-invoice", pdf.Build(), "application/pdf");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        Assert.Equal("DOCUMENT_BUSY", await ReadProblemCodeAsync(response));
    }

    private sealed class ExpiringPdfClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Add(ref _timestamp, TimeSpan.FromSeconds(4).Ticks);
    }

    [Fact]
    public async Task CompanyLockContentionReturnsRetryAfterAndDocumentBusyOverHttp()
    {
        await using var blocker = new SqliteConnection($"Data Source={_databasePath}");
        await blocker.OpenAsync();
        await using var held = blocker.BeginTransaction(deferred: false);
        using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
        request.Content = CreateUploadForm("purchase-invoice", CreatePng(0xFF334455), "image/png");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        Assert.Equal("DOCUMENT_BUSY", await ReadProblemCodeAsync(response));
    }

    [Fact]
    public async Task CrossCompanyCaptureAndLinkTargetIdsMatchMissingIdResponses()
    {
        string foreignCaptureId;
        string foreignBillId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            db.SuppressTenantSecurityCheck = true;
            var suffix = Guid.NewGuid().ToString("N");
            var company = new Company
            {
                Name = "Foreign capture workspace", CompanyCode = "X" + suffix[..5], Slug = "foreign-" + suffix[..8]
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            foreignCaptureId = PublicIdGenerator.New("cap");
            db.DocumentCaptures.Add(new Core.Domain.DocumentCapture
            {
                CompanyId = company.Id, PublicId = foreignCaptureId, Kind = "purchase-invoice", SchemaVersion = 1,
                Status = "ready", SourceHash = new string('d', 64), ContentHash = new string('e', 64),
                NormalizationVersion = "image-normalization-v2", ContentType = "image/png", ByteCount = 3,
                PageCount = 1, RequestFingerprint = new string('f', 64), ExtractionGeneration = 1,
                CreatedBy = "foreign-user", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            foreignBillId = PublicIdGenerator.New("bill");
            db.PurchaseInvoiceHeaders.Add(new PurchaseInvoiceHeader
            {
                CompanyId = company.Id, PublicId = foreignBillId, Date = new DateTime(2026, 11, 20), VendorInvoiceNo = "FOREIGN-1"
            });
            await db.SaveChangesAsync();
        }

        using var missingCaptureRequest = NewRequest(HttpMethod.Get, "/api/document-captures/cap_00000000000000000000000000");
        using var missingCaptureResponse = await _client.SendAsync(missingCaptureRequest);
        using var foreignCaptureRequest = NewRequest(HttpMethod.Get, $"/api/document-captures/{foreignCaptureId}");
        using var foreignCaptureResponse = await _client.SendAsync(foreignCaptureRequest);
        Assert.Equal(HttpStatusCode.NotFound, foreignCaptureResponse.StatusCode);
        Assert.Equal(await missingCaptureResponse.Content.ReadAsByteArrayAsync(), await foreignCaptureResponse.Content.ReadAsByteArrayAsync());

        var ownCaptureId = (await UploadAsync(CreatePng(0xFF668822), "cross-company-link")).GetProperty("id").GetString()!;
        using (var acceptRequest = NewRequest(HttpMethod.Post, $"/api/document-captures/{ownCaptureId}/accept"))
        {
            acceptRequest.Content = JsonContent.Create(new { fields = new { date = "2026-11-20", vendorName = "Vendor" } });
            using var acceptResponse = await _client.SendAsync(acceptRequest);
            Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        }
        var missingBillId = PublicIdGenerator.New("bill");
        using var missingLinkRequest = NewRequest(HttpMethod.Post, $"/api/document-captures/{ownCaptureId}/link");
        missingLinkRequest.Content = JsonContent.Create(new { targetType = "bill", targetId = missingBillId });
        using var missingLinkResponse = await _client.SendAsync(missingLinkRequest);
        using var foreignLinkRequest = NewRequest(HttpMethod.Post, $"/api/document-captures/{ownCaptureId}/link");
        foreignLinkRequest.Content = JsonContent.Create(new { targetType = "bill", targetId = foreignBillId });
        using var foreignLinkResponse = await _client.SendAsync(foreignLinkRequest);
        Assert.Equal(HttpStatusCode.NotFound, foreignLinkResponse.StatusCode);
        Assert.Equal(await missingLinkResponse.Content.ReadAsByteArrayAsync(), await foreignLinkResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ProviderFailureReturns422WithCapturePublicIdOverHttp()
    {
        var extractor = Assert.IsType<Api.DocumentCaptures.FakeDocumentFieldExtractor>(
            _factory.Services.GetRequiredService<Api.DocumentCaptures.IDocumentFieldExtractor>());
        var fixtures = (IDictionary<string, string>)typeof(Api.DocumentCaptures.FakeDocumentFieldExtractor)
            .GetField("_fixtures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(extractor)!;
        var original = fixtures["purchase-invoice"];
        fixtures["purchase-invoice"] = "{\"date\":\"not-a-date\"}";
        try
        {
            using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
            request.Content = CreateUploadForm("purchase-invoice", CreatePng(0xFF998877), "image/png");
            using var response = await _client.SendAsync(request);
            Assert.True(response.StatusCode == (HttpStatusCode)422,
                $"Expected provider failure response, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("DOCUMENT_PROVIDER_OUTPUT_INVALID", body.RootElement.GetProperty("code").GetString());
            Assert.StartsWith("cap_", body.RootElement.GetProperty("captureId").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            fixtures["purchase-invoice"] = original;
        }
    }

    [Fact]
    public async Task HostedFeatureDenialUsesTheStandardEntitlementProblemOverHttp()
    {
        var denial = new EntitlementDecision(false, "FEATURE_NOT_ENTITLED", "Upgrade required.",
            RequiredFeature: FeatureKeys.DocumentCapture, UpgradeAvailable: true, EffectivePlan: "hosted");
        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(service => service.Get(It.IsAny<int>())).Returns(new EntitlementSnapshot(true, "hosted", "hosted",
            false, [], [], [], null, new HashSet<string>(), "hosted"));
        entitlement.Setup(service => service.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns((int _, EntitlementAction action) => action is EntitlementAction.Feature ? denial : new EntitlementDecision(true, null, null));
        entitlement.SetupGet(service => service.Options).Returns(new EntitlementsOptions { SupportEmail = "support@example.test" });

        using var hostedFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEntitlementService>();
            services.AddSingleton(entitlement.Object);
        }));
        using var hostedClient = hostedFactory.CreateClient();
        using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
        request.Content = CreateUploadForm("purchase-invoice", CreatePng(0xFF224466), "image/png");
        using var response = await hostedClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FEATURE_NOT_ENTITLED", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("support@example.test", problem.RootElement.GetProperty("contact").GetString());
        Assert.True(problem.RootElement.GetProperty("upgradeAvailable").GetBoolean());
        Assert.Equal(FeatureKeys.DocumentCapture, problem.RootElement.GetProperty("requiredFeature").GetString());
        Assert.True(problem.RootElement.TryGetProperty("type", out _));
        Assert.True(problem.RootElement.TryGetProperty("title", out _));
        Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task RuntimeFlagOffKeepsExistingEvidenceRoutesAvailableOverHttp()
    {
        var accepted = await UploadAsync(CreatePng(0xFF123321), "flag-off-accepted");
        var acceptedId = accepted.GetProperty("id").GetString()!;
        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{acceptedId}/accept"))
        {
            request.Content = JsonContent.Create(new { fields = new { date = "2026-11-20", vendorName = "Review" } });
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var readyForAccept = (await UploadAsync(CreatePng(0xFF223322), "flag-off-block-accept")).GetProperty("id").GetString()!;
        var readyForReject = (await UploadAsync(CreatePng(0xFF323322), "flag-off-reject")).GetProperty("id").GetString()!;
        var billId = await SeedBillAsync();

        using var disabledFactory = _factory.WithWebHostBuilder(builder => builder.UseSetting("DocumentCapture:Enabled", "false"));
        using var disabledClient = disabledFactory.CreateClient();
        using (var request = NewRequest(HttpMethod.Post, "/api/document-captures"))
        {
            request.Content = CreateUploadForm("purchase-invoice", CreatePng(0xFF423322), "image/png");
            using var response = await disabledClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("DOCUMENT_CAPTURE_UNCONFIGURED", await ReadProblemCodeAsync(response));
        }
        foreach (var path in new[] { $"/api/document-captures/{acceptedId}", $"/api/document-captures/{acceptedId}/bytes" })
        {
            using var request = NewRequest(HttpMethod.Get, path);
            using var response = await disabledClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{readyForAccept}/accept"))
        {
            request.Content = JsonContent.Create(new { fields = new { date = "2026-11-20", vendorName = "Review" } });
            using var response = await disabledClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("DOCUMENT_CAPTURE_DISABLED", await ReadProblemCodeAsync(response));
        }
        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{readyForReject}/reject"))
        using (var response = await disabledClient.SendAsync(request))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var request = NewRequest(HttpMethod.Post, $"/api/document-captures/{acceptedId}/link"))
        {
            request.Content = JsonContent.Create(new { targetType = "bill", targetId = billId });
            using var response = await disabledClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static MultipartFormDataContent CreateUploadForm(string kind, byte[] bytes, string contentType)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind), "Kind");
        form.Add(new StringContent("1"), "SchemaVersion");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "File", "document.bin");
        return form;
    }

    private async Task<string> UploadFileAsync(byte[] bytes, string contentType, string key)
    {
        using var request = NewRequest(HttpMethod.Post, "/api/document-captures");
        request.Headers.Add("Idempotency-Key", key);
        request.Content = CreateUploadForm("purchase-invoice", bytes, contentType);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }

    private async Task<string> UploadKindAsync(string token, string kind, byte[] bytes, string key)
    {
        using var request = NewRequestForToken(HttpMethod.Post, "/api/document-captures", token);
        request.Headers.Add("Idempotency-Key", key);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind), "Kind");
        form.Add(new StringContent("1"), "SchemaVersion");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "File", "assistant.png");
        request.Content = form;
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
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
        => NewRequestForToken(method, path, _token);

    private HttpRequestMessage NewRequestForToken(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    [Fact]
    public async Task ExtractionHostFalse_RejectsUploadsWithoutReservationOrWorker_WhileOtherCaptureEndpointsWork()
    {
        var capToAccept = await UploadWithoutKeyAsync(CreatePng(0xFF112233u));
        var capToReject = await UploadWithoutKeyAsync(CreatePng(0xFF445566u));
        var billId = await SeedBillAsync();

        var spyExtractor = new Mock<Api.DocumentCaptures.IDocumentFieldExtractor>();
        spyExtractor.SetupGet(e => e.IsConfigured).Returns(true);
        spyExtractor.SetupGet(e => e.ProviderName).Returns("spy");

        using var nonHostFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DocumentCapture:ExtractionHost", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Api.DocumentCaptures.IDocumentFieldExtractor>();
                services.AddSingleton(spyExtractor.Object);
            });
        });
        using var nonHostClient = nonHostFactory.CreateClient();

        // 1. Capabilities still reports enabled and providerAvailable, and ignores unknown query params like ?warmup=1.
        foreach (var capPath in new[] { "/api/document-captures/capabilities", "/api/document-captures/capabilities?warmup=1" })
        {
            using var capReq = NewRequest(HttpMethod.Get, capPath);
            using var capRes = await nonHostClient.SendAsync(capReq);
            Assert.Equal(HttpStatusCode.OK, capRes.StatusCode);
            using var body = JsonDocument.Parse(await capRes.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("enabled").GetBoolean());
            Assert.True(body.RootElement.GetProperty("providerAvailable").GetBoolean());
        }

        var (beforeAttempts, beforeCaptures) = await CountCaptureRowsAsync();

        // 2. Normal upload and forced re-upload both return 503 DOCUMENT_CAPTURE_UNCONFIGURED with Retry-After: 1
        // without creating reservations, quota attempts, or calling the extractor.
        foreach (var force in new[] { false, true })
        {
            using var uploadReq = NewRequest(HttpMethod.Post, "/api/document-captures");
            uploadReq.Headers.Add("Idempotency-Key", $"non-host-{force}");
            using var form = CreateUploadForm("purchase-invoice", CreatePng(0xFF778899u), "image/png");
            if (force)
                form.Add(new StringContent("true"), "Force");
            uploadReq.Content = form;

            using var uploadRes = await nonHostClient.SendAsync(uploadReq);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, uploadRes.StatusCode);
            Assert.Equal("1", uploadRes.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0") ?? uploadRes.Headers.GetValues("Retry-After").FirstOrDefault());
            Assert.Equal("DOCUMENT_CAPTURE_UNCONFIGURED", await ReadProblemCodeAsync(uploadRes));
        }

        Assert.Equal((beforeAttempts, beforeCaptures), await CountCaptureRowsAsync());
        spyExtractor.Verify(e => e.ExtractAsync(It.IsAny<Api.DocumentCaptures.DocumentExtractionRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // 3. Evidence reads (metadata + bytes), accept, link, and reject still work on ExtractionHost=false.
        using (var getReq = NewRequest(HttpMethod.Get, $"/api/document-captures/{capToAccept}"))
        using (var getRes = await nonHostClient.SendAsync(getReq))
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        using (var imgReq = NewRequest(HttpMethod.Get, $"/api/document-captures/{capToAccept}/image"))
        using (var imgRes = await nonHostClient.SendAsync(imgReq))
            Assert.Equal(HttpStatusCode.OK, imgRes.StatusCode);

        using (var acceptReq = NewRequest(HttpMethod.Post, $"/api/document-captures/{capToAccept}/accept"))
        {
            acceptReq.Content = JsonContent.Create(new { fields = new { date = "2026-11-20", vendorName = "Fixture Vendor" } });
            using var acceptRes = await nonHostClient.SendAsync(acceptReq);
            Assert.Equal(HttpStatusCode.OK, acceptRes.StatusCode);
        }

        using (var linkReq = NewRequest(HttpMethod.Post, $"/api/document-captures/{capToAccept}/links"))
        {
            linkReq.Content = JsonContent.Create(new { targetType = "bill", targetId = billId });
            using var linkRes = await nonHostClient.SendAsync(linkReq);
            Assert.Equal(HttpStatusCode.OK, linkRes.StatusCode);
        }

        using (var rejectReq = NewRequest(HttpMethod.Post, $"/api/document-captures/{capToReject}/reject"))
        using (var rejectRes = await nonHostClient.SendAsync(rejectReq))
            Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        async Task<(int Attempts, int Captures)> CountCaptureRowsAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var attempts = await db.DocumentCaptureAttempts.IgnoreQueryFilters().CountAsync(row => row.CompanyId == _companyId);
            var captures = await db.DocumentCaptures.IgnoreQueryFilters().CountAsync(row => row.CompanyId == _companyId);
            return (attempts, captures);
        }
    }

    [Fact]
    public async Task EnabledFalseAndExtractionHostFalse_StartsCleanly_ReportsDisabledCapabilities_AndRejectsUpload()
    {
        using var disabledNonHostFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DocumentCapture:Enabled", "false");
            builder.UseSetting("DocumentCapture:ExtractionHost", "false");
        });
        using var client = disabledNonHostFactory.CreateClient();

        using (var capReq = NewRequest(HttpMethod.Get, "/api/document-captures/capabilities"))
        using (var capRes = await client.SendAsync(capReq))
        {
            Assert.Equal(HttpStatusCode.OK, capRes.StatusCode);
            using var body = JsonDocument.Parse(await capRes.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("enabled").GetBoolean());
        }

        using var uploadReq = NewRequest(HttpMethod.Post, "/api/document-captures");
        uploadReq.Content = CreateUploadForm("purchase-invoice", CreatePng(0xFF99AABB), "image/png");
        using var uploadRes = await client.SendAsync(uploadReq);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, uploadRes.StatusCode);
        Assert.Equal("1", uploadRes.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0") ?? uploadRes.Headers.GetValues("Retry-After").FirstOrDefault());
        Assert.Equal("DOCUMENT_CAPTURE_UNCONFIGURED", await ReadProblemCodeAsync(uploadRes));
    }
}
