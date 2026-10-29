// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Dto.Banking;

namespace Services.Banking
{
    public class BankReconciliationService : IBankReconciliationService
    {
        private readonly IRepository<BankReconciliation> _recons;
        private readonly IRepository<BankReconciliationItem> _items;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly IRepository<GeneralLedgerHeader> _glHeaders;
        private readonly IBankTransactionService _transactions;
        private readonly ITenantContext _tenant;

        public BankReconciliationService(
            IRepository<BankReconciliation> recons,
            IRepository<BankReconciliationItem> items,
            IRepository<Bank> banks,
            IRepository<GeneralLedgerLine> glLines,
            IRepository<GeneralLedgerHeader> glHeaders,
            IBankTransactionService transactions,
            ITenantContext tenant)
        {
            _recons = recons;
            _items = items;
            _banks = banks;
            _glLines = glLines;
            _glHeaders = glHeaders;
            _transactions = transactions;
            _tenant = tenant;
        }

        public IReadOnlyList<BankReconciliationListItemDto> List(int bankId)
        {
            EnsureCompany();
            RequireBank(bankId);
            return _recons.Table
                .Where(r => r.BankId == bankId)
                .OrderByDescending(r => r.StatementEndDate)
                .ThenByDescending(r => r.Id)
                .ToList()
                .Select(ToListItem)
                .ToList();
        }

        public BankReconciliationDto Get(int id)
        {
            EnsureCompany();
            return ToDetail(RequireRecon(id));
        }

        public BankReconciliationDto Start(StartBankReconciliationRequest request, string actor)
        {
            EnsureCompany();
            var bank = RequireActiveBank(request.BankId);
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            var endDate = request.StatementEndDate.Date;
            var endingBalance = Round2(request.StatementEndingBalance);

            var existingDraft = _recons.Table.FirstOrDefault(r =>
                r.BankId == bank.Id && r.Status == BankReconciliationStatuses.Draft);
            if (existingDraft != null)
                throw new BankReconciliationConflictException(
                    BankReconciliationConflictException.DraftExistsCode,
                    "A draft reconciliation already exists for this account. Resume it instead.",
                    existingDraft.Id);

            var lastFinished = LatestFinished(bank.Id);
            if (lastFinished != null && endDate <= lastFinished.StatementEndDate.Date)
                throw new InvalidOperationException(
                    $"Statement end date must be after the last finished reconciliation ({lastFinished.StatementEndDate:yyyy-MM-dd}).");

            var beginning = lastFinished?.StatementEndingBalance ?? 0m;

            var recon = new BankReconciliation
            {
                CompanyId = _tenant.RequireCompanyId(),
                BankId = bank.Id,
                StatementEndDate = endDate,
                StatementEndingBalance = endingBalance,
                BeginningBalance = Round2(beginning),
                Status = BankReconciliationStatuses.Draft,
                CreatedBy = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(),
                CreatedAt = DateTime.UtcNow,
                Version = 1
            };
            _recons.Insert(recon);

            // §6: opening-balance lines are pre-ticked deposits on the first (and later) reconciliations.
            foreach (var line in CandidateLines(bank, endDate).Where(IsOpeningBalanceLine))
                InsertItem(recon, line);

            return ToDetail(recon);
        }

        public BankReconciliationDto UpdateItems(int id, UpdateBankReconciliationItemsRequest request)
        {
            EnsureCompany();
            var recon = RequireDraft(id, request.Version);
            var bank = RequireBank(recon.BankId);
            var candidates = CandidateLines(bank, recon.StatementEndDate.Date)
                .ToDictionary(l => l.Id);

            foreach (var lineId in request.Remove ?? Array.Empty<int>())
            {
                var item = _items.Table.FirstOrDefault(i =>
                    i.BankReconciliationId == recon.Id && i.GeneralLedgerLineId == lineId);
                if (item != null)
                    _items.Delete(item);
            }

            foreach (var lineId in request.Add ?? Array.Empty<int>())
            {
                if (_items.Table.Any(i => i.BankReconciliationId == recon.Id && i.GeneralLedgerLineId == lineId))
                    continue;
                if (!candidates.TryGetValue(lineId, out var line))
                    throw new InvalidOperationException(
                        "That line is not available to tick (wrong account, already cleared, or after the statement end date).");
                InsertItem(recon, line);
            }

            BumpVersion(recon);
            return ToDetail(recon);
        }

        public BankReconciliationDto AddAdjustment(int id, CreateReconciliationAdjustmentRequest request)
        {
            EnsureCompany();
            var recon = RequireDraft(id, request.Version);
            var bank = RequireBank(recon.BankId);
            var date = request.Date.Date;
            if (date > recon.StatementEndDate.Date)
                throw new InvalidOperationException("Adjustment date must be on or before the statement end date.");
            if (lastFinishedEnd(bank.Id) is { } lastEnd && date <= lastEnd)
                throw new InvalidOperationException("Adjustment date must be after the last finished reconciliation.");

            var kind = (BankTransactionKinds)request.Kind;
            if (kind is not (BankTransactionKinds.Fee or BankTransactionKinds.Interest or BankTransactionKinds.Adjustment))
                throw new InvalidOperationException("Pick Fee, Interest, or Adjustment.");

            var created = _transactions.Create(new CreateBankTransactionRequest
            {
                Kind = request.Kind,
                BankId = bank.Id,
                Date = date,
                Amount = request.Amount,
                ContraAccountId = request.ContraAccountId,
                IncreaseBalance = request.IncreaseBalance,
                Memo = request.Memo,
                ReferenceNo = request.ReferenceNo
            });

            if (!created.GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("The adjustment did not post a ledger entry.");

            var line = _glLines.Table.FirstOrDefault(l =>
                l.GeneralLedgerHeaderId == created.GeneralLedgerHeaderId.Value
                && l.AccountId == bank.AccountId);
            if (line == null)
                throw new InvalidOperationException("Could not find the bank line for the adjustment.");

            InsertItem(recon, line);
            BumpVersion(recon);
            return ToDetail(recon);
        }

        public BankReconciliationDto Finish(int id, FinishBankReconciliationRequest request, string actor)
        {
            EnsureCompany();
            var recon = RequireRecon(id);
            if (recon.Status != BankReconciliationStatuses.Draft || recon.Version != request.Version)
                throw new BankReconciliationConflictException(
                    BankReconciliationConflictException.ConcurrentFinishCode,
                    "Someone else finished or changed this reconciliation.");

            var bank = RequireBank(recon.BankId);
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            var latestDraftOrFinished = _recons.Table
                .Where(r => r.BankId == bank.Id &&
                            (r.Status == BankReconciliationStatuses.Draft || r.Status == BankReconciliationStatuses.Finished))
                .OrderByDescending(r => r.StatementEndDate)
                .ThenByDescending(r => r.Id)
                .FirstOrDefault();
            if (latestDraftOrFinished == null || latestDraftOrFinished.Id != recon.Id)
                throw new BankReconciliationConflictException(
                    BankReconciliationConflictException.NotLatestCode,
                    "This is no longer the latest reconciliation for the account.");

            var totals = ComputeTotals(recon);
            if (totals.Difference != 0m)
                throw new InvalidOperationException(
                    $"Difference must be 0.00 before finishing (currently {totals.Difference:0.00}).");

            var items = _items.Table.Where(i => i.BankReconciliationId == recon.Id).ToList();
            foreach (var item in items)
            {
                var line = _glLines.GetById(item.GeneralLedgerLineId)
                    ?? throw new InvalidOperationException("A ticked line no longer exists.");
                if (line.AccountId != bank.AccountId.Value)
                    throw new InvalidOperationException("A ticked line is not on this bank account.");
                if (line.BankReconciliationId.HasValue)
                    throw new InvalidOperationException("A ticked line was cleared by another reconciliation.");
                var header = _glHeaders.GetById(line.GeneralLedgerHeaderId)
                    ?? throw new InvalidOperationException("A ticked line has no ledger header.");
                if (header.Date.Date > recon.StatementEndDate.Date)
                    throw new InvalidOperationException("A ticked line is dated after the statement end date.");

                line.BankReconciliationId = recon.Id;
                _glLines.Update(line);
            }

            recon.Status = BankReconciliationStatuses.Finished;
            recon.FinishedBy = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
            recon.FinishedAt = DateTime.UtcNow;
            BumpVersion(recon);
            return ToDetail(recon);
        }

        public BankReconciliationDto Undo(int id, UndoBankReconciliationRequest request, string actor)
        {
            EnsureCompany();
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidOperationException("A reason is required to undo a reconciliation.");

            var recon = RequireRecon(id);
            if (recon.Status != BankReconciliationStatuses.Finished)
                throw new InvalidOperationException("Only a finished reconciliation can be undone.");

            var latest = LatestFinished(recon.BankId);
            if (latest == null || latest.Id != recon.Id)
                throw new BankReconciliationConflictException(
                    BankReconciliationConflictException.NotLatestCode,
                    "Only the latest finished reconciliation for the account can be undone.");

            var stamped = _glLines.Table.Where(l => l.BankReconciliationId == recon.Id).ToList();
            foreach (var line in stamped)
            {
                line.BankReconciliationId = null;
                _glLines.Update(line);
            }

            recon.Status = BankReconciliationStatuses.Undone;
            recon.UndoneBy = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
            recon.UndoneAt = DateTime.UtcNow;
            recon.UndoReason = request.Reason.Trim();
            BumpVersion(recon);
            return ToDetail(recon);
        }

        public void DiscardDraft(int id)
        {
            EnsureCompany();
            var recon = RequireRecon(id);
            if (recon.Status != BankReconciliationStatuses.Draft)
                throw new InvalidOperationException("Only a draft reconciliation can be discarded.");

            foreach (var item in _items.Table.Where(i => i.BankReconciliationId == recon.Id).ToList())
                _items.Delete(item);
            _recons.Delete(recon);
        }

        private void InsertItem(BankReconciliation recon, GeneralLedgerLine line)
        {
            _items.Insert(new BankReconciliationItem
            {
                CompanyId = recon.CompanyId,
                BankReconciliationId = recon.Id,
                GeneralLedgerLineId = line.Id,
                Amount = line.Amount,
                DrCr = line.DrCr
            });
        }

        private void BumpVersion(BankReconciliation recon)
        {
            recon.Version += 1;
            _recons.Update(recon);
        }

        private BankReconciliation RequireDraft(int id, long expectedVersion)
        {
            var recon = RequireRecon(id);
            if (recon.Status != BankReconciliationStatuses.Draft || recon.Version != expectedVersion)
                throw new BankReconciliationConflictException(
                    BankReconciliationConflictException.ConcurrentFinishCode,
                    "Someone else finished or changed this reconciliation.");
            return recon;
        }

        private BankReconciliation RequireRecon(int id)
        {
            var recon = _recons.GetById(id);
            if (recon == null || recon.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(BankReconciliation), id);
            return recon;
        }

        private Bank RequireActiveBank(int bankId)
        {
            var bank = RequireBank(bankId);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("That bank account is archived.");
            return bank;
        }

        private Bank RequireBank(int bankId)
        {
            var bank = _banks.GetById(bankId);
            if (bank == null || bank.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(Bank), bankId);
            return bank;
        }

        private BankReconciliation? LatestFinished(int bankId) =>
            _recons.Table
                .Where(r => r.BankId == bankId && r.Status == BankReconciliationStatuses.Finished)
                .OrderByDescending(r => r.StatementEndDate)
                .ThenByDescending(r => r.Id)
                .FirstOrDefault();

        private DateTime? lastFinishedEnd(int bankId) =>
            LatestFinished(bankId)?.StatementEndDate.Date;

        private IReadOnlyList<GeneralLedgerLine> CandidateLines(Bank bank, DateTime endDate)
        {
            if (!bank.AccountId.HasValue)
                return Array.Empty<GeneralLedgerLine>();

            var accountId = bank.AccountId.Value;
            return (
                from line in _glLines.Table
                join header in _glHeaders.Table on line.GeneralLedgerHeaderId equals header.Id
                where line.AccountId == accountId
                      && line.BankReconciliationId == null
                      && header.Date.Date <= endDate
                orderby header.Date, header.Id, line.Id
                select line
            ).ToList();
        }

        private bool IsOpeningBalanceLine(GeneralLedgerLine line)
        {
            var header = _glHeaders.GetById(line.GeneralLedgerHeaderId);
            if (header == null)
                return false;
            if (header.DocumentType != DocumentTypes.BankTransaction)
                return false;
            return string.Equals(header.Description, BankAccountService.OpeningBalanceDescription, StringComparison.Ordinal);
        }

        private (decimal ClearedDeposits, decimal ClearedPayments, decimal ClearedBalance, decimal Difference) ComputeTotals(BankReconciliation recon)
        {
            var items = _items.Table.Where(i => i.BankReconciliationId == recon.Id).ToList();
            var deposits = Round2(items.Where(i => i.DrCr == DrOrCrSide.Dr).Sum(i => i.Amount));
            var payments = Round2(items.Where(i => i.DrCr == DrOrCrSide.Cr).Sum(i => i.Amount));
            var clearedBalance = Round2(recon.BeginningBalance + deposits - payments);
            var difference = Round2(recon.StatementEndingBalance - clearedBalance);
            return (deposits, payments, clearedBalance, difference);
        }

        private BankReconciliationDto ToDetail(BankReconciliation recon)
        {
            var bank = _banks.GetById(recon.BankId);
            var totals = ComputeTotals(recon);
            var ticked = _items.Table
                .Where(i => i.BankReconciliationId == recon.Id)
                .Select(i => i.GeneralLedgerLineId)
                .ToHashSet();

            var candidates = recon.Status == BankReconciliationStatuses.Draft && bank != null
                ? CandidateLines(bank, recon.StatementEndDate.Date)
                : FinishedSnapshotLines(recon);

            var deposits = new List<BankReconciliationCandidateLineDto>();
            var payments = new List<BankReconciliationCandidateLineDto>();
            foreach (var line in candidates)
            {
                var header = _glHeaders.GetById(line.GeneralLedgerHeaderId);
                var dto = new BankReconciliationCandidateLineDto
                {
                    LineId = line.Id,
                    HeaderId = line.GeneralLedgerHeaderId,
                    Date = header?.Date ?? DateTime.MinValue,
                    Type = IsOpeningBalanceLine(line) && header != null
                        ? $"Opening balance (as of {header.Date:yyyy-MM-dd})"
                        : (header?.DocumentType.ToString() ?? "Line"),
                    Reference = header?.Description,
                    In = line.DrCr == DrOrCrSide.Dr ? line.Amount : 0m,
                    Out = line.DrCr == DrOrCrSide.Cr ? line.Amount : 0m,
                    Ticked = ticked.Contains(line.Id) || recon.Status != BankReconciliationStatuses.Draft,
                    IsOpeningBalance = IsOpeningBalanceLine(line)
                };
                if (line.DrCr == DrOrCrSide.Dr)
                    deposits.Add(dto);
                else
                    payments.Add(dto);
            }

            // Finished: only show the snapshot (ticked items); for draft, candidates include unticked.
            if (recon.Status != BankReconciliationStatuses.Draft)
            {
                deposits = deposits.Where(d => d.Ticked).ToList();
                payments = payments.Where(p => p.Ticked).ToList();
            }

            return new BankReconciliationDto
            {
                Id = recon.Id,
                BankId = recon.BankId,
                BankName = bank?.Name,
                StatementEndDate = recon.StatementEndDate,
                StatementEndingBalance = recon.StatementEndingBalance,
                BeginningBalance = recon.BeginningBalance,
                Status = (int)recon.Status,
                StatusLabel = StatusLabel(recon.Status),
                Version = recon.Version,
                ClearedDeposits = totals.ClearedDeposits,
                ClearedPayments = totals.ClearedPayments,
                ClearedBalance = totals.ClearedBalance,
                Difference = totals.Difference,
                CreatedBy = recon.CreatedBy,
                CreatedAt = recon.CreatedAt,
                FinishedBy = recon.FinishedBy,
                FinishedAt = recon.FinishedAt,
                Deposits = deposits,
                Payments = payments
            };
        }

        private IReadOnlyList<GeneralLedgerLine> FinishedSnapshotLines(BankReconciliation recon)
        {
            var lineIds = _items.Table
                .Where(i => i.BankReconciliationId == recon.Id)
                .Select(i => i.GeneralLedgerLineId)
                .ToList();
            if (lineIds.Count == 0)
                return Array.Empty<GeneralLedgerLine>();
            return _glLines.Table.Where(l => lineIds.Contains(l.Id)).ToList();
        }

        private BankReconciliationListItemDto ToListItem(BankReconciliation recon)
        {
            var bank = _banks.GetById(recon.BankId);
            return new BankReconciliationListItemDto
            {
                Id = recon.Id,
                BankId = recon.BankId,
                BankName = bank?.Name,
                StatementEndDate = recon.StatementEndDate,
                StatementEndingBalance = recon.StatementEndingBalance,
                BeginningBalance = recon.BeginningBalance,
                Status = (int)recon.Status,
                StatusLabel = StatusLabel(recon.Status),
                FinishedAt = recon.FinishedAt,
                FinishedBy = recon.FinishedBy,
                Version = recon.Version
            };
        }

        private static string StatusLabel(BankReconciliationStatuses status) => status switch
        {
            BankReconciliationStatuses.Draft => "Draft",
            BankReconciliationStatuses.Finished => "Finished",
            BankReconciliationStatuses.Undone => "Undone",
            _ => status.ToString()
        };

        private static decimal Round2(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private void EnsureCompany()
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("No company context.");
            _ = _tenant.RequireCompanyId();
        }
    }
}
