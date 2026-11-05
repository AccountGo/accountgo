#nullable enable
using System;

namespace Core.Domain;

/// <summary>Global registry joining two independently posted workspace journals.</summary>
public sealed class LinkedEntryPair : BaseEntity
{
    public int SourceCompanyId { get; set; }
    public int? SourceJournalId { get; set; }
    public int MirrorCompanyId { get; set; }
    public int? MirrorJournalId { get; set; }
    public int WorkBankAccountId { get; set; }
    public int? StatementLineId { get; set; }
    public string OwnerUserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public decimal SourceAmount { get; set; }
    public decimal MirrorAmount { get; set; }
    public string SourceCurrency { get; set; } = "";
    public string MirrorCurrency { get; set; } = "";
    public decimal ExchangeRate { get; set; } = 1m;
    public string State { get; set; } = "";
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
