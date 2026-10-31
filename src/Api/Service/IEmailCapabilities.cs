using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Tenancy;

namespace Api.Service;

/// <summary>Organization-scoped email capabilities; availability does not grant dispatch authorization.</summary>
public sealed record EmailCapabilities(
    int CompanyId, bool CustomEmailAvailable, bool CanManageDomains, bool CanManageApprovedSenders,
    string State, string? UnavailableReason, bool UpgradeAvailable = false,
    IReadOnlyList<ApprovedEmailSender>? ApprovedSenders = null);

/// <summary>A safe approved identity offered by the current organization.</summary>
public sealed record ApprovedEmailSender(string Id, string Address, string DisplayName, string DomainId, bool Ready);

/// <summary>Requires an already resolved organization and authenticated actor.</summary>
public interface IEmailCapabilities
{
    Task<EmailCapabilities> GetAsync(string actorId, CancellationToken ct = default);
}

/// <summary>Community builds retain platform sending without exposing custom sender support.</summary>
public sealed class PlatformEmailCapabilities(ITenantContext tenant) : IEmailCapabilities
{
    public Task<EmailCapabilities> GetAsync(string actorId, CancellationToken ct = default) =>
        Task.FromResult(new EmailCapabilities(tenant.RequireCompanyId(), false, false, false,
            "unavailable", "Custom email is unavailable.", ApprovedSenders: System.Array.Empty<ApprovedEmailSender>()));
}
