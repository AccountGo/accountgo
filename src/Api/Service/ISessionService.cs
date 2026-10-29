// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Generic;
using System.Threading.Tasks;
using Api.Data;
using Dto.Security;

namespace Api.Service;

public interface ISessionService
{
    Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionAsync(
        ApplicationUser user,
        string? ipAddress,
        string? userAgent,
        string? mfaMethod = null,
        int? trustedDeviceId = null,
        IEnumerable<string>? amr = null,
        bool mfaSatisfied = false);

    Task<(bool Success, AuthResponse? Response, string? RawRefreshToken, string? Error)> CreateSessionForUserAsync(
        ApplicationUser user,
        string? ipAddress,
        string? userAgent,
        string? amr = null);

    Task<(bool Success, AuthResponse? Response, string? NewRawRefreshToken, string? Error)> RefreshSessionAsync(
        string rawRefreshToken,
        string? ipAddress,
        string? userAgent);

    Task<bool> RevokeSessionAsync(string rawRefreshToken);

    Task<bool> RevokeAllSessionsForUserAsync(string userId);

    Task<UserDto> BuildUserDtoAsync(ApplicationUser user);
}
