// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;

namespace Api.Service;

public enum MfaGateResult
{
    NotRequired,
    Challenge,
    Enrollment,
    ChallengeRequired = Challenge,
    EnrollmentRequired = Enrollment
}

public sealed record MfaGateDecision(
    MfaGateResult Result,
    string? Policy,
    IReadOnlyList<string> Methods);

public interface IMfaGate
{
    Task<MfaGateDecision> EvaluateAfterFactorAAsync(
        ApplicationUser user,
        string factorAMethod,
        string? trustedDeviceRawToken,
        CancellationToken ct = default);
}

public class CommunityMfaGate : IMfaGate
{
    public Task<MfaGateDecision> EvaluateAfterFactorAAsync(
        ApplicationUser user,
        string factorAMethod,
        string? trustedDeviceRawToken,
        CancellationToken ct = default)
    {
        return Task.FromResult(new MfaGateDecision(
            MfaGateResult.NotRequired,
            null,
            Array.Empty<string>()));
    }
}
