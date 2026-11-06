#nullable enable
using System;

namespace Core.Domain.Auditing;

/// <summary>Immutable before/after evidence for a tenant-owned record change.</summary>
public sealed class RecordChange : ICompanyOwned
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ChangesJson { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string ActorKind { get; set; } = "system";
    public string CorrelationId { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }
}
