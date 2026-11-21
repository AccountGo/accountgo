#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Api.Data;
using Api.PublicIds;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AccountGo.BackendTests.PublicIds;

internal static class PublicIdTestHelper
{
    public static int ResolveCompanyId(IServiceProvider services, string publicId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return db.Companies.IgnoreQueryFilters().Where(c => c.PublicId == publicId).Select(c => c.Id).Single();
    }

    public static string PublicIdOf<TEntity>(IServiceProvider services, int id)
        where TEntity : BaseEntity, IHasPublicId
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return db.Set<TEntity>().IgnoreQueryFilters().Where(e => e.Id == id).Select(e => e.PublicId).Single();
    }

    public static int InternalIdOf<TEntity>(IServiceProvider services, string publicId)
        where TEntity : BaseEntity, IHasPublicId
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        return db.Set<TEntity>().IgnoreQueryFilters().Where(e => e.PublicId == publicId).Select(e => e.Id).Single();
    }

    private static readonly AsyncLocalHttpContextAccessor SharedAccessor = new();
    private static readonly JsonSerializerSettings SharedSettings = new()
    {
        ContractResolver = new PublicIdContractResolver(SharedAccessor)
        {
            NamingStrategy = new CamelCaseNamingStrategy()
        }
    };

    /// <summary>
    /// Serializes a request body (including DTOs with [PublicId] / [PublicIdByType] integer fields)
    /// into wire JSON carrying real prefixed public ID strings.
    /// </summary>
    public static StringContent ToJsonContent(IServiceProvider services, int companyId, object body)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var tenant = sp.GetRequiredService<ITenantContext>();
        using var _ = companyId > 0 ? tenant.Impersonate(companyId, role: CompanyMemberRole.Owner) : null;
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var prev = SharedAccessor.HttpContext;
        SharedAccessor.HttpContext = httpContext;
        try
        {
            var registry = sp.GetRequiredService<PublicIdRegistry>();
            var resolver = sp.GetRequiredService<IPublicIdResolver>();
            PublicIdPrefetchFilter.PrefetchObjectGraphAsync(body, resolver, registry).GetAwaiter().GetResult();
            var json = JsonConvert.SerializeObject(body, SharedSettings);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }
        finally
        {
            SharedAccessor.HttpContext = prev;
        }
    }

    /// <summary>
    /// Deserializes wire JSON (carrying prefixed public ID strings) into a DTO type whose integer ID fields
    /// are annotated with [PublicId] / [PublicIdByType].
    /// </summary>
    public static async Task<T?> ReadDtoAsync<T>(IServiceProvider services, int companyId, HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var tenant = sp.GetRequiredService<ITenantContext>();
        using var _ = companyId > 0 ? tenant.Impersonate(companyId, role: CompanyMemberRole.Owner) : null;
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var prev = SharedAccessor.HttpContext;
        SharedAccessor.HttpContext = httpContext;
        try
        {
            return JsonConvert.DeserializeObject<T>(raw, SharedSettings);
        }
        finally
        {
            SharedAccessor.HttpContext = prev;
        }
    }

    private sealed class AsyncLocalHttpContextAccessor : IHttpContextAccessor
    {
        private readonly System.Threading.AsyncLocal<HttpContext?> _current = new();
        public HttpContext? HttpContext
        {
            get => _current.Value;
            set => _current.Value = value;
        }
    }
}
