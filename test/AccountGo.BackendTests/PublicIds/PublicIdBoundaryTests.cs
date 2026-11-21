// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Api.Data;
using Api.PublicIds;
using Core.Domain;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace AccountGo.BackendTests.PublicIds;

public sealed class PublicIdBoundaryTests
{
    private sealed class SampleDto
    {
        [PublicId("inv")]
        public int Id { get; set; }

        [PublicId("cus")]
        public int? CustomerId { get; set; }

        [PublicId("inv")]
        public List<int>? RelatedInvoiceIds { get; set; }
    }

    private sealed class SamplePolyDto
    {
        public string EntityType { get; set; } = string.Empty;

        [PublicIdByType(nameof(EntityType))]
        public int? EntityId { get; set; }
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static (SqliteConnection Conn, ApiDbContext ApiDb, ApplicationIdentityDbContext IdDb, ServiceProvider Sp, Api.Tenancy.TenantContext Tenant, IHttpContextAccessor Accessor) CreateHarness()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        var tenant = new Api.Tenancy.TenantContext();
        tenant.SetTenant(1, "co1", CompanyMemberRole.Owner, Array.Empty<string>());
        var apiDb = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options, tenant);
        apiDb.SuppressTenantSecurityCheck = true;
        var apiCreator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)apiDb.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        apiCreator.CreateTables();

        var idDb = new ApplicationIdentityDbContext(new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(conn).Options);
        var idCreator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)idDb.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        idCreator.CreateTables();

        var services = new ServiceCollection();
        var accessor = new HttpContextAccessor();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddSingleton<IHostEnvironment>(new TestHostEnv());
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(apiDb);
        services.AddSingleton(idDb);
        services.AddSingleton<PublicIdRegistry>();
        services.AddScoped<IPublicIdResolver, PublicIdResolver>();
        services.AddSingleton<PublicIdContractResolver>();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        accessor.HttpContext = new DefaultHttpContext { RequestServices = sp };
        return (conn, apiDb, idDb, sp, tenant, accessor);
    }

    [Fact]
    public void Converter_SerializesAndDeserializesPublicIds_AndRejectsNumbersAndForeignIds()
    {
        var (conn, apiDb, idDb, sp, tenant, accessor) = CreateHarness();
        using var _ = conn;
        using var __ = apiDb;
        using var ___ = idDb;
        using var ____ = sp;

        var co1 = new Company { Name = "Co1", ShortName = "C1", Slug = "co1" };
        var co2 = new Company { Name = "Co2", ShortName = "C2", Slug = "co2" };
        apiDb.Companies.AddRange(co1, co2);
        apiDb.SaveChanges();

        var cus1 = new Customer { CompanyId = co1.Id, No = "C1" };
        var inv1 = new SalesInvoiceHeader { CompanyId = co1.Id, Customer = cus1, Date = DateTime.UtcNow };
        var cus2 = new Customer { CompanyId = co2.Id, No = "C2" };
        var inv2 = new SalesInvoiceHeader { CompanyId = co2.Id, Customer = cus2, Date = DateTime.UtcNow };
        apiDb.SalesInvoiceHeaders.AddRange(inv1, inv2);
        apiDb.SaveChanges();

        tenant.SetTenant(co1.Id, co1.Slug!, CompanyMemberRole.Owner, Array.Empty<string>());
        var contractResolver = sp.GetRequiredService<PublicIdContractResolver>();
        var settings = new JsonSerializerSettings { ContractResolver = contractResolver };

        // Write int -> public string
        var dto = new SampleDto { Id = inv1.Id, CustomerId = cus1.Id, RelatedInvoiceIds = new List<int> { inv1.Id } };
        var json = JsonConvert.SerializeObject(dto, settings);
        Assert.Contains(inv1.PublicId, json);
        Assert.Contains(cus1.PublicId, json);

        // Read valid public string -> int
        var roundTrip = JsonConvert.DeserializeObject<SampleDto>(json, settings)!;
        Assert.Equal(inv1.Id, roundTrip.Id);
        Assert.Equal(cus1.Id, roundTrip.CustomerId);
        Assert.Equal(new[] { inv1.Id }, roundTrip.RelatedInvoiceIds);

        // Read bare JSON number -> invalid_id
        var numEx = Assert.Throws<PublicIdSerializationException>(() =>
            JsonConvert.DeserializeObject<SampleDto>($"{{\"id\":{inv1.Id}}}", settings));
        Assert.Equal(PublicIdErrorCodes.InvalidId, numEx.Code);

        // Read wrong prefix -> invalid_id
        var wrongEx = Assert.Throws<PublicIdSerializationException>(() =>
            JsonConvert.DeserializeObject<SampleDto>($"{{\"id\":\"{cus1.PublicId}\"}}", settings));
        Assert.Equal(PublicIdErrorCodes.InvalidId, wrongEx.Code);

        // Read foreign workspace ID -> unknown_id (same message as random unknown ID)
        var foreignEx = Assert.Throws<PublicIdSerializationException>(() =>
            JsonConvert.DeserializeObject<SampleDto>($"{{\"id\":\"{inv2.PublicId}\"}}", settings));
        var unknownEx = Assert.Throws<PublicIdSerializationException>(() =>
            JsonConvert.DeserializeObject<SampleDto>($"{{\"id\":\"{PublicIdGenerator.New("inv")}\"}}", settings));
        Assert.Equal(PublicIdErrorCodes.UnknownId, foreignEx.Code);
        Assert.Equal(PublicIdErrorCodes.UnknownId, unknownEx.Code);
        Assert.Equal(unknownEx.Message, foreignEx.Message);

        // Without HttpContext -> throws (persistence guard)
        accessor.HttpContext = null;
        Assert.Throws<InvalidOperationException>(() => JsonConvert.SerializeObject(dto, settings));
    }

    [Fact]
    public void PolymorphicConverter_TranslatesMappedTypes_NullsExemptTypes_AndThrowsOnUnmappedInTests()
    {
        var (conn, apiDb, idDb, sp, tenant, _) = CreateHarness();
        using var _ = conn;
        using var __ = apiDb;
        using var ___ = idDb;
        using var ____ = sp;

        var co1 = new Company { Name = "Co1", ShortName = "C1", Slug = "co1" };
        apiDb.Companies.Add(co1);
        apiDb.SaveChanges();
        var cus1 = new Customer { CompanyId = co1.Id, No = "C1" };
        var inv1 = new SalesInvoiceHeader { CompanyId = co1.Id, Customer = cus1, Date = DateTime.UtcNow };
        apiDb.SalesInvoiceHeaders.Add(inv1);
        apiDb.SaveChanges();
        tenant.SetTenant(co1.Id, co1.Slug!, CompanyMemberRole.Owner, Array.Empty<string>());

        var settings = new JsonSerializerSettings { ContractResolver = sp.GetRequiredService<PublicIdContractResolver>() };

        var mappedJson = JsonConvert.SerializeObject(new SamplePolyDto { EntityType = "SalesInvoice", EntityId = inv1.Id }, settings);
        Assert.Contains(inv1.PublicId, mappedJson);

        var exemptJson = JsonConvert.SerializeObject(new SamplePolyDto { EntityType = "CompanySetting", EntityId = 42 }, settings);
        Assert.Contains("\"entityId\":null", exemptJson);

        Assert.Throws<InvalidOperationException>(() =>
            JsonConvert.SerializeObject(new SamplePolyDto { EntityType = "DefinitelyNotAnEntity", EntityId = 42 }, settings));
    }

    [Fact]
    public void ErrorFilter_ReturnsByteIdentical404ForForeignAndUnknownIds_And400ForInvalidId()
    {
        var filter = new PublicIdErrorFilter();
        Assert.True(filter.Order <= -3000);

        ActionExecutingContext MakeCtx(string code, string msg)
        {
            var http = new DefaultHttpContext();
            PublicIdErrorRecorder.Record(http, code, msg, "id");
            var ac = new ActionContext(http, new RouteData(), new ActionDescriptor());
            return new ActionExecutingContext(ac, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        }

        var foreignCtx = MakeCtx(PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage);
        var unknownCtx = MakeCtx(PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage);
        filter.OnActionExecuting(foreignCtx);
        filter.OnActionExecuting(unknownCtx);

        var r1 = Assert.IsType<NotFoundObjectResult>(foreignCtx.Result);
        var r2 = Assert.IsType<NotFoundObjectResult>(unknownCtx.Result);
        Assert.Equal(JsonConvert.SerializeObject(r1.Value), JsonConvert.SerializeObject(r2.Value));

        var badCtx = MakeCtx(PublicIdErrorCodes.InvalidId, "IDs are strings like 'inv_…'.");
        filter.OnActionExecuting(badCtx);
        Assert.IsType<BadRequestObjectResult>(badCtx.Result);
    }

    [Fact]
    public void AllPublicIdAttributesInDtoAndApi_ReferenceRegisteredPrefixes()
    {
        var registry = new PublicIdRegistry();
        var assemblies = new[] { typeof(PublicIdAttribute).Assembly, typeof(ApiDbContext).Assembly };

        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var attr = prop.GetCustomAttribute<PublicIdAttribute>(inherit: false);
                    if (attr != null)
                    {
                        Assert.True(
                            registry.TryGetByPrefix(attr.Prefix, out _),
                            $"Property {type.FullName}.{prop.Name} uses unregistered prefix '{attr.Prefix}'.");
                    }
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    foreach (var param in method.GetParameters())
                    {
                        var attr = param.GetCustomAttribute<PublicIdAttribute>(inherit: false);
                        if (attr != null)
                        {
                            Assert.True(
                                registry.TryGetByPrefix(attr.Prefix, out _),
                                $"Parameter {type.FullName}.{method.Name}({param.Name}) uses unregistered prefix '{attr.Prefix}'.");
                        }
                    }
                }
            }
        }
    }
}
