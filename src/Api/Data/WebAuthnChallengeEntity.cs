// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;

namespace Api.Data
{
    public class WebAuthnChallengeEntity
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty; // "registration" or "assertion"
        public string JsonPayload { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
    }
}
