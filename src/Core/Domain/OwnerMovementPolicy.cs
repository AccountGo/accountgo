// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
namespace Core.Domain
{
    /// <summary>An equity account and the entity-specific action offered for an owner movement.</summary>
    public sealed record OwnerMovementDirection(string AccountCode, string Label);

    /// <summary>Offers personal capital movements only for sole proprietors and partnerships.</summary>
    public static class OwnerMovementPolicy
    {
        public static OwnerMovementDirection? Drawing(EntityType type) => type switch
        {
            EntityType.SoleProprietor => new("30800", "Owner drawing"),
            EntityType.Partnership => new("30800", "Partner drawing"),
            _ => null
        };

        public static OwnerMovementDirection? Contribution(EntityType type) => type switch
        {
            EntityType.SoleProprietor => new("30100", "Owner contribution"),
            EntityType.Partnership => new("30100", "Partner contribution"),
            _ => null
        };
    }
}
