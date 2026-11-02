// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    /// <summary>
    /// Durable per-company coordination row acquired first in every policy-sensitive transaction.
    /// A concurrency token alone is insufficient across instances; writers lock this row.
    /// </summary>
    [Table("WorkspacePolicyCoordination")]
    public class WorkspacePolicyCoordination : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public virtual Company Company { get; set; } = null!;

        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastAcquiredAtUtc { get; set; }
    }
}
