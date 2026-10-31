using System.Collections.Generic;

namespace Dto.Email;

/// <summary>Provider-neutral organization email settings. Credentials are never part of this contract.</summary>
public sealed record EmailSettingsDto(IReadOnlyList<EmailDomainSettingsDto> Domains, IReadOnlyList<EmailSenderSettingsDto> Senders);
public sealed record EmailDomainSettingsDto(string Id, string Name, string State, string? Reason,
    string OwnershipHost, string OwnershipValue, long ChallengeExpiresAt, bool OwnershipConsumed,
    long? OwnershipCheckedAt, long? ProviderCheckedAt, string ProviderStatus, string Region, string ZoneName,
    IReadOnlyList<EmailDnsRecordDto> Records, bool AdministrativelySuspended, string ProvisioningState = "None");
public sealed record EmailDnsRecordDto(string Purpose, string Type, string Host, string RelativeHost,
    string Value, string Ttl, int? Priority, string Status);
public sealed record EmailSenderSettingsDto(string Id, string DomainId, string Address, string DisplayName, bool Approved, long ApprovedAt);
