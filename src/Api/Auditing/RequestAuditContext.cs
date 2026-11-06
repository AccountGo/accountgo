using Core.Authorization;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Diagnostics;

namespace Api.Auditing;

public interface IRequestAuditContext
{
    string? ActorUserId { get; }
    string ActorKind { get; }
    string CorrelationId { get; }
    string? IpAddress { get; }
}

public sealed class RequestAuditContext : IRequestAuditContext
{
    private readonly IHttpContextAccessor _http;
    private readonly ITenantContext _tenant;

    public RequestAuditContext(IHttpContextAccessor http, ITenantContext tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    public string? ActorUserId => _tenant.UserId
        ?? _http.HttpContext?.User.FindFirst("sub")?.Value
        ?? _http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public string ActorKind => _tenant.ActorDomain switch
    {
        ActorTrustDomain.StaffOperator or ActorTrustDomain.PlatformAdministrator => "staff",
        ActorTrustDomain.ScopedService or ActorTrustDomain.ServicePrincipal => "job",
        ActorTrustDomain.WorkspaceUser => "user",
        _ => _http.HttpContext is null ? "system" : (_http.HttpContext.User.Identity?.IsAuthenticated == true ? "user" : "system")
    };
    public string CorrelationId => _http.HttpContext?.Items[Middleware.CorrelationIdMiddleware.ItemKey] as string
        ?? Activity.Current?.TraceId.ToString()
        ?? "background";
    public string? IpAddress => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
