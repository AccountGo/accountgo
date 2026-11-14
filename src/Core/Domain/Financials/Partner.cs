// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace Core.Domain.Financials;

public sealed class Partner : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public int CapitalAccountId { get; set; }
    public int DrawingsAccountId { get; set; }
    public DateTime CreatedAt { get; set; }
    [Required, StringLength(256)] public string CreatedBy { get; set; } = "";
    public DateTime? ArchivedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class PartnerShare : BaseEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public int FinancialYearId { get; set; }
    public int PartnerId { get; set; }
    public decimal Percent { get; set; }
}
