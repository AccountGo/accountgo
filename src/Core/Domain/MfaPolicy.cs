// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace Core.Domain
{
    public enum MfaPolicy
    {
        Optional = 0,
        RequiredForAdmins = 1,
        RequiredForAll = 2
    }
}
