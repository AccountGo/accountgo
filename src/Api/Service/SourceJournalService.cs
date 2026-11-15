//-----------------------------------------------------------------------
// <copyright file="SourceJournalService.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Services.Financial;

namespace Api.Service;

public enum SourceJournalDraftState
{
    Created = 0,
    Existing = 1,
    Conflict = 2
}

public sealed record SourceJournalDraftLine(
    int AccountId,
    DrOrCrSide DrCr,
    decimal Amount,
    string? Memo = null);

public sealed record SourceJournalDraftRequest(
    int CompanyId,
    string SourceSystem,
    string SourceKey,
    string PreparerId,
    DateTime Date,
    string? Memo,
    string? ReferenceNo,
    IReadOnlyList<SourceJournalDraftLine> Lines,
    JournalVoucherTypes VoucherType = JournalVoucherTypes.Other);

public sealed record SourceJournalDraftResult(
    int JournalId,
    bool Created,
    SourceJournalDraftState State,
    string Code,
    string? Message = null);

public sealed class SourceJournalOperationException : InvalidOperationException
{
    public string Code { get; }
    public int StatusCode { get; }

    public SourceJournalOperationException(string code, string message, int statusCode = 409)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}

public static class SourceJournalTestGate
{
    public static Action<int, string, string>? BeforeCreateInsert { get; set; }
    public static Action<int, int>? BeforeDiscardDelete { get; set; }

    public static void Reset()
    {
        BeforeCreateInsert = null;
        BeforeDiscardDelete = null;
    }
}

/// <summary>
/// Creates idempotent source-referenced journal drafts for server-side importers and discards
/// unposted imported drafts so their source key can be re-issued.
/// </summary>
public sealed class SourceJournalService
{
    private const int MaxSourceSystemLength = 32;
    private const int MaxSourceKeyLength = 200;
    private const string SystemPreparerPrefix = "system:";

    private readonly ApiDbContext _db;
    private readonly Services.Financial.IFinancialService _financial;
    private readonly ITenantContext _tenant;

    public SourceJournalService(
        ApiDbContext db,
        Services.Financial.IFinancialService financial,
        ITenantContext tenant)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _financial = financial ?? throw new ArgumentNullException(nameof(financial));
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
    }

    public SourceJournalDraftResult CreateDraft(SourceJournalDraftRequest request)
    {
        ValidateRequest(request);

        var requestedFingerprint = AccessJournalFingerprint.Compute(
            request.Date,
            request.Memo,
            (int)request.VoucherType,
            request.Lines.Select(l => (l.AccountId, l.DrCr, l.Amount, l.Memo)),
            request.SourceSystem,
            request.SourceKey);

        var existing = FindExisting(request.CompanyId, request.SourceSystem, request.SourceKey);
        if (existing != null)
            return MatchOrConflict(existing, requestedFingerprint, request.ReferenceNo);

        SourceJournalTestGate.BeforeCreateInsert?.Invoke(request.CompanyId, request.SourceSystem, request.SourceKey);

        using var tx = _db.Database.BeginTransaction();
        try
        {
            var header = new JournalEntryHeader
            {
                CompanyId = request.CompanyId,
                Date = request.Date,
                Memo = request.Memo,
                ReferenceNo = request.ReferenceNo,
                VoucherType = request.VoucherType,
                SourceSystem = request.SourceSystem,
                SourceKey = request.SourceKey,
                Posted = false
            };

            foreach (var line in request.Lines)
            {
                header.JournalEntryLines.Add(new JournalEntryLine
                {
                    CompanyId = request.CompanyId,
                    AccountId = line.AccountId,
                    DrCr = line.DrCr,
                    Amount = line.Amount,
                    Memo = line.Memo
                });
            }

            _financial.AddJournalEntry(header);

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = request.CompanyId,
                UserName = request.PreparerId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)AuditEventTypes.Added,
                TableName = "__JournalEntryPreparer",
                RecordId = header.Id.ToString(CultureInfo.InvariantCulture),
                FieldName = "PreparedByUserId",
                OriginalValue = string.Empty,
                NewValue = request.PreparerId
            });

            _db.SaveChanges();
            tx.Commit();

            return new SourceJournalDraftResult(
                header.Id,
                Created: true,
                SourceJournalDraftState.Created,
                Code: "created");
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            try { tx.Rollback(); } catch { /* ignore rollback errors after failed commit */ }
            _db.ChangeTracker.Clear();

            var raced = FindExisting(request.CompanyId, request.SourceSystem, request.SourceKey);
            if (raced != null)
                return MatchOrConflict(raced, requestedFingerprint, request.ReferenceNo);

            throw;
        }
    }

    public void Discard(int companyId, int journalId, string actorId)
    {
        if (!_tenant.CompanyId.HasValue || _tenant.CompanyId.Value != companyId)
            throw new InvalidOperationException("CompanyId must match the active tenant context.");

        if (string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("ActorId is required.", nameof(actorId));

        var header = _db.JournalEntryHeaders
            .AsNoTracking()
            .FirstOrDefault(j => j.CompanyId == companyId && j.Id == journalId);

        if (header == null)
            throw new SourceJournalOperationException("not_found", $"Journal entry #{journalId} not found.", 404);

        if (string.IsNullOrWhiteSpace(header.SourceSystem))
            throw new SourceJournalOperationException("not_a_source_journal", "Only imported source journals can be discarded.", 409);

        if (header.Posted == true || header.GeneralLedgerHeaderId.HasValue)
            throw new SourceJournalOperationException("source_journal_posted", "Posted journals cannot be discarded.", 409);

        var sourceRef = $"{header.SourceSystem}:{header.SourceKey}";

        SourceJournalTestGate.BeforeDiscardDelete?.Invoke(companyId, journalId);

        using var tx = _db.Database.BeginTransaction();
        _db.JournalEntryLines
            .Where(l => l.CompanyId == companyId
                && l.JournalEntryHeaderId == journalId
                && _db.JournalEntryHeaders.Any(h => h.Id == journalId
                    && h.CompanyId == companyId
                    && h.SourceSystem != null
                    && h.Posted != true
                    && h.GeneralLedgerHeaderId == null))
            .ExecuteDelete();

        var deleted = _db.JournalEntryHeaders
            .Where(h => h.Id == journalId
                && h.CompanyId == companyId
                && h.SourceSystem != null
                && h.Posted != true
                && h.GeneralLedgerHeaderId == null)
            .ExecuteDelete();

        if (deleted != 1)
        {
            tx.Rollback();
            var current = _db.JournalEntryHeaders
                .AsNoTracking()
                .FirstOrDefault(j => j.CompanyId == companyId && j.Id == journalId);
            if (current == null)
                throw new SourceJournalOperationException("not_found", $"Journal entry #{journalId} not found.", 404);
            if (current.Posted == true || current.GeneralLedgerHeaderId.HasValue)
                throw new SourceJournalOperationException("source_journal_posted", "Posted journals cannot be discarded.", 409);
            throw new SourceJournalOperationException("not_a_source_journal", "Only imported source journals can be discarded.", 409);
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserName = actorId,
            AuditEventDateUTC = DateTime.UtcNow,
            AuditEventType = (int)AuditEventTypes.Deleted,
            TableName = "__SourceJournalDiscarded",
            RecordId = journalId.ToString(CultureInfo.InvariantCulture),
            FieldName = "SourceKey",
            OriginalValue = sourceRef,
            NewValue = string.Empty
        });
        _db.SaveChanges();
        tx.Commit();
    }

    private void ValidateRequest(SourceJournalDraftRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (!_tenant.CompanyId.HasValue || _tenant.CompanyId.Value != request.CompanyId)
            throw new InvalidOperationException("CompanyId must match the active tenant context.");

        if (!IsValidSourceSystem(request.SourceSystem))
            throw new ArgumentException("SourceSystem must be 1-32 characters of lowercase [a-z0-9-].", nameof(request));

        if (!IsValidSourceKey(request.SourceKey))
            throw new ArgumentException("SourceKey must be 1-200 printable ASCII characters with no whitespace.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.PreparerId)
            || !request.PreparerId.StartsWith(SystemPreparerPrefix, StringComparison.Ordinal)
            || request.PreparerId.Length <= SystemPreparerPrefix.Length)
            throw new ArgumentException("PreparerId must start with 'system:' and include a non-empty identifier.", nameof(request));

        if (!Enum.IsDefined(typeof(JournalVoucherTypes), request.VoucherType))
            throw new ArgumentException("VoucherType is not valid.", nameof(request));

        if (request.Lines == null || request.Lines.Count < 2)
            throw new ArgumentException("At least two journal lines are required.", nameof(request));

        decimal totalDr = 0m;
        decimal totalCr = 0m;
        var accountIds = new HashSet<int>();

        foreach (var line in request.Lines)
        {
            if (line.Amount <= 0m)
                throw new ArgumentException("Every journal line amount must be greater than zero.", nameof(request));

            if (line.DrCr == DrOrCrSide.Dr)
                totalDr += line.Amount;
            else if (line.DrCr == DrOrCrSide.Cr)
                totalCr += line.Amount;
            else
                throw new ArgumentException("Every journal line must specify Dr or Cr.", nameof(request));

            accountIds.Add(line.AccountId);
        }

        if (totalDr != totalCr || totalDr <= 0m)
            throw new ArgumentException("Journal lines must balance (debits must equal credits).", nameof(request));

        var accounts = _db.Accounts
            .IgnoreQueryFilters()
            .Include(a => a.ChildAccounts)
            .Where(a => a.CompanyId == request.CompanyId && accountIds.Contains(a.Id))
            .ToList();

        if (accounts.Count != accountIds.Count)
            throw new ArgumentException("Every account must belong to the target company.", nameof(request));

        foreach (var account in accounts)
        {
            if (account.ArchivedAt.HasValue || !account.CanPost())
                throw new ArgumentException($"Account {account.AccountCode} ({account.AccountName}) cannot be posted to.", nameof(request));
        }
    }

    private JournalEntryHeader? FindExisting(int companyId, string sourceSystem, string sourceKey) =>
        _db.JournalEntryHeaders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(j => j.JournalEntryLines)
            .FirstOrDefault(j => j.CompanyId == companyId
                && j.SourceSystem == sourceSystem
                && j.SourceKey == sourceKey);

    private static SourceJournalDraftResult MatchOrConflict(
        JournalEntryHeader existing,
        string requestedFingerprint,
        string? requestedReferenceNo)
    {
        var existingFingerprint = AccessJournalFingerprint.Compute(
            existing.Date,
            existing.Memo,
            (int)(existing.VoucherType ?? 0),
            existing.JournalEntryLines.Select(l => (l.AccountId, l.DrCr, l.Amount, (string?)l.Memo)),
            existing.SourceSystem,
            existing.SourceKey);

        var sameReference = string.Equals(
            existing.ReferenceNo ?? string.Empty,
            requestedReferenceNo ?? string.Empty,
            StringComparison.Ordinal);

        if (string.Equals(existingFingerprint, requestedFingerprint, StringComparison.Ordinal) && sameReference)
        {
            return new SourceJournalDraftResult(
                existing.Id,
                Created: false,
                SourceJournalDraftState.Existing,
                Code: "existing");
        }

        return new SourceJournalDraftResult(
            existing.Id,
            Created: false,
            SourceJournalDraftState.Conflict,
            Code: "source_key_conflict",
            Message: "A journal entry with this source key already exists with different content.");
    }

    private static bool IsValidSourceSystem(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxSourceSystemLength)
            return false;

        foreach (var c in value)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
                return false;
        }

        return true;
    }

    private static bool IsValidSourceKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxSourceKeyLength)
            return false;

        foreach (var c in value)
        {
            if (c < '!' || c > '~')
                return false;
        }

        return true;
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            var typeName = cur.GetType().Name;
            if (typeName == "SqliteException")
            {
                var prop = cur.GetType().GetProperty("SqliteErrorCode");
                if (prop?.GetValue(cur) is int code && code == 19)
                    return true;
            }
            if (typeName == "PostgresException")
            {
                var prop = cur.GetType().GetProperty("SqlState");
                if (prop?.GetValue(cur) as string == "23505")
                    return true;
            }
            if (cur.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || cur.Message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
