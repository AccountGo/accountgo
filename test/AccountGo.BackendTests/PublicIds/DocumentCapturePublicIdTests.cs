using Api.PublicIds;
using Api.Data;
using Core.Domain;
using Xunit;

namespace AccountGo.BackendTests.PublicIds;

public sealed class DocumentCapturePublicIdTests
{
    [Fact]
    public void Registry_RegistersCaptureAsTenantOwnedPublicEntity()
    {
        var registry = new PublicIdRegistry();

        Assert.True(registry.TryGetByPrefix("cap", out var registration));
        Assert.Equal("DocumentCapture", registration.EntityType.Name);
        Assert.Equal(PublicIdScopeRule.TenantOwned, registration.ScopeRule);
        Assert.True(typeof(ICompanyOwned).IsAssignableFrom(registration.EntityType));
    }

    [Fact]
    public void CaptureStorageEntities_AreNotIncludedInRecordChangeAuditAllowList()
    {
        Assert.DoesNotContain(typeof(Core.Domain.DocumentCapture), RecordChangeInterceptor.AllowList.Keys);
        Assert.DoesNotContain(typeof(DocumentCaptureBlob), RecordChangeInterceptor.AllowList.Keys);
        Assert.DoesNotContain(typeof(DocumentCaptureAttempt), RecordChangeInterceptor.AllowList.Keys);
        Assert.DoesNotContain(typeof(DocumentCaptureLink), RecordChangeInterceptor.AllowList.Keys);
    }
}
