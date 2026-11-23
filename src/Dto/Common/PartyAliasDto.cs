// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Dto.Common
{
    /// <summary>A normalized name alias remembered for a vendor or client from prior document captures.</summary>
    public class PartyAliasDto
    {
        public string Alias { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
