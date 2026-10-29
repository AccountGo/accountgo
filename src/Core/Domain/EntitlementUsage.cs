// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    /// <summary>
    /// One metered creation (e.g. an invoice) for plan limits. Kept separately from the document so backdating,
    /// renumbering or deleting a document cannot change what was used in a month.
    /// </summary>
    [Table("EntitlementUsage")]
    public class EntitlementUsage : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        /// <summary><see cref="Core.Entitlements.MeteredResources.Key"/> of the resource.</summary>
        public string Resource { get; set; } = string.Empty;
        public int? EntityId { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
