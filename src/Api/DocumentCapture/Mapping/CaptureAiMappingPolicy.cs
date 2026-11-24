// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
namespace Api.DocumentCaptures.Mapping;

/// <summary>
/// Decides whether a workspace may have its documents' text sent to a field mapper. A host that adds consent, plan and metering
/// registers its own policy; the default allows nothing, so AI mapping is off everywhere unless a host opts in.
/// </summary>
public interface ICaptureAiMappingPolicy
{
    Task<bool> IsAllowedAsync(int companyId, string kind, CancellationToken cancellationToken);
}

public sealed class DisabledCaptureAiMappingPolicy : ICaptureAiMappingPolicy
{
    public Task<bool> IsAllowedAsync(int companyId, string kind, CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>A mapper that also names itself, so mapped results can be cached per mapper version.</summary>
public interface IVersionedCaptureFieldMapper : ICaptureFieldMapper
{
    string Name { get; }
    string Version { get; }
}
