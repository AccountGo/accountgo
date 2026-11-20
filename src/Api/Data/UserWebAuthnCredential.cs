using Core.Domain;
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;

namespace Api.Data
{
    [PublicIdPrefix("pkey")]
    public class UserWebAuthnCredential : IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public byte[] CredentialId { get; set; } = Array.Empty<byte>();
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();
        public byte[] UserHandle { get; set; } = Array.Empty<byte>();
        public uint SignatureCounter { get; set; }
        public string? CredType { get; set; }
        public Guid AaGuid { get; set; }
        public string Nickname { get; set; } = "Passkey";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; }
        public string? Transports { get; set; }
    }
}
