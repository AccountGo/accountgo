// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain;

[Table("PartyAliases")]
public class PartyAlias : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public int PartyId { get; set; }
    /// <summary>"vendor" or "customer".</summary>
    public string Role { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public virtual Party? Party { get; set; }
}
