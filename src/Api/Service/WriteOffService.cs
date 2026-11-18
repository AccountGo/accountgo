// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Sales;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public sealed class WriteOffService
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly Services.Financial.IFinancialService _financial;
        private readonly SettlementCommandCoordinator _coordinator;
        private readonly SettlementAccountMappingService _mappings;

        public WriteOffService(
            ApiDbContext db,
            ITenantContext tenant,
            Services.Financial.IFinancialService financial,
            SettlementCommandCoordinator coordinator,
            SettlementAccountMappingService mappings)
        {
            _db = db;
            _tenant = tenant;
            _financial = financial;
            _coordinator = coordinator;
            _mappings = mappings;
        }

        public ReceivableWriteOffDto RequestWriteOff(RequestWriteOffCommand request)
        {
            return _coordinator.Execute(
                "write_off.request",
                request.IdempotencyKey,
                request,
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (request.Amount <= 0 || !CurrencyPrecisionRules.HasValidPrecision(request.Amount, precision))
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Write-off amount must be positive.", 400);
                    if (request.AllowanceAllocatedAmount < 0 || request.AllowanceAllocatedAmount > request.Amount)
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Allocated allowance must be between zero and the write-off amount.", 400);
                    if (string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.CollectionEvidence))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "Reason and collection evidence are required for a receivable write-off.", 400);

                    var invoice = _db.SalesInvoiceHeaders
                        .Include(i => i.SalesInvoiceLines)
                        .Include(i => i.CustomerAllocations)
                        .FirstOrDefault(i => i.CompanyId == companyId && i.Id == request.SalesInvoiceHeaderId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Sales invoice not found.", 404);

                    if (invoice.Status is null or SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Cannot write off a draft or void invoice.", 400);
                    if (request.ExpectedInvoiceVersion.HasValue && invoice.Version != request.ExpectedInvoiceVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Invoice changed concurrently.", 409);
                    if (request.Date.Date < invoice.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Write-off date cannot precede invoice date.", 400);

                    var due = CurrencyPrecisionRules.RoundMoney(invoice.BalanceDue(), precision);
                    if (request.Amount > due)
                        throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Write-off amount ({request.Amount}) exceeds invoice due ({due}).", 409);

                    var actor = _tenant.UserId ?? "system";
                    var wo = new ReceivableWriteOff
                    {
                        CompanyId = companyId,
                        CustomerId = invoice.CustomerId,
                        SalesInvoiceHeaderId = invoice.Id,
                        Date = request.Date.Date,
                        CurrencyCode = setting.Currency,
                        Amount = request.Amount,
                        AllowanceAllocatedAmount = request.AllowanceAllocatedAmount,
                        ImpairmentShortfallAmount = CurrencyPrecisionRules.RoundMoney(request.Amount - request.AllowanceAllocatedAmount, precision),
                        Reason = request.Reason.Trim(),
                        CollectionEvidence = request.CollectionEvidence.Trim(),
                        Status = SettlementDocumentStatus.PendingReview,
                        SubmittedBy = actor,
                        SubmittedAtUtc = DateTime.UtcNow,
                        Version = 1
                    };

                    _db.ReceivableWriteOffs.Add(wo);
                    _db.SaveChanges();
                    return BuildDto(companyId, wo.Id);
                },
                dto => dto.Id,
                nameof(ReceivableWriteOff));
        }

        public ReceivableWriteOffDto Post(int id, PostWriteOffCommand request)
        {
            return _coordinator.Execute(
                "write_off.post",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    var wo = _db.ReceivableWriteOffs.FirstOrDefault(w => w.CompanyId == companyId && w.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Write-off request not found.", 404);
                    if (wo.Status is not (SettlementDocumentStatus.Draft or SettlementDocumentStatus.PendingReview))
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only draft or pending-review write-offs can be posted.", 409);
                    if (request.ExpectedVersion.HasValue && wo.Version != request.ExpectedVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Write-off changed concurrently.", 409);

                    var actor = _tenant.UserId ?? "system";
                    _coordinator.EnsureReviewPolicyForPosting(companyId, wo.SubmittedBy, actor, requiresTeamReview: true);

                    var invoice = _db.SalesInvoiceHeaders
                        .Include(i => i.SalesInvoiceLines)
                        .Include(i => i.CustomerAllocations)
                        .First(i => i.CompanyId == companyId && i.Id == wo.SalesInvoiceHeaderId);

                    var due = CurrencyPrecisionRules.RoundMoney(invoice.BalanceDue(), precision);
                    if (wo.Amount > due)
                        throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Write-off ({wo.Amount}) exceeds current invoice due ({due}).", 409);

                    var customer = _db.Customers.AsNoTracking().First(c => c.CompanyId == companyId && c.Id == invoice.CustomerId);
                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var arAccId = _mappings.RequireActiveAccount(companyId, invoice.ReceivableAccountId ?? customer.AccountsReceivableAccountId, "Accounts Receivable");
                    var allowanceAccId = _mappings.RequireActiveAccount(companyId, glSetting?.ReceivableLossAllowanceAccountId, "Allowance for Receivable Losses");
                    var impairmentAccId = wo.ImpairmentShortfallAmount > 0
                        ? _mappings.RequireActiveAccount(companyId, glSetting?.ImpairmentExpenseAccountId, "Impairment / Bad-Debt Expense")
                        : (int?)null;

                    invoice.ReceivableAccountId ??= arAccId;
                    wo.ReceivableAccountId = arAccId;
                    wo.LossAllowanceAccountId = allowanceAccId;
                    wo.ImpairmentExpenseAccountId = impairmentAccId;
                    wo.No ??= GenerateNumber("WO", _db.ReceivableWriteOffs.Where(w => w.CompanyId == companyId && w.No != null).Select(w => w.No!));

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.ReceivableWriteOff,
                        wo.Date,
                        $"Receivable Write-Off {wo.No} (Invoice {invoice.No})",
                        wo.Id);

                    if (wo.ImpairmentShortfallAmount > 0)
                    {
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, impairmentAccId!.Value, wo.ImpairmentShortfallAmount));
                        glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, allowanceAccId, wo.ImpairmentShortfallAmount));
                    }

                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, allowanceAccId, wo.Amount));
                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, arAccId, wo.Amount));
                    _financial.SaveGeneralLedgerEntry(glHeader);

                    wo.GeneralLedgerHeaderId = glHeader.Id;
                    wo.Status = SettlementDocumentStatus.Posted;
                    wo.ReviewedBy = actor;
                    wo.ReviewedAtUtc = DateTime.UtcNow;
                    wo.PostedBy = actor;
                    wo.PostedAtUtc = wo.ReviewedAtUtc;
                    wo.Version++;

                    invoice.WrittenOffAmount = CurrencyPrecisionRules.RoundMoney(invoice.WrittenOffAmount + wo.Amount, precision);
                    invoice.Version++;
                    invoice.RefreshPaymentStatus();

                    _db.SaveChanges();
                    return BuildDto(companyId, wo.Id);
                },
                dto => dto.Id,
                nameof(ReceivableWriteOff));
        }

        public ReceivableWriteOffDto Reverse(int id, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "write_off.reverse",
                request.IdempotencyKey,
                new { id, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var wo = _db.ReceivableWriteOffs.FirstOrDefault(w => w.CompanyId == companyId && w.Id == id)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Write-off not found.", 404);
                    if (wo.Status != SettlementDocumentStatus.Posted || !wo.GeneralLedgerHeaderId.HasValue)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted write-offs can be reversed.", 409);
                    if (wo.RecoveredAmount > 0)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Reverse recoveries before reversing the write-off.", 409);
                    if (request.ReversalDate.Date < wo.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede write-off date.", 400);

                    var revGl = _financial.CreateReversingEntry(
                        wo.GeneralLedgerHeaderId.Value,
                        $"Reverse write-off {wo.No}: {request.Reason.Trim()}",
                        request.ReversalDate.Date);
                    _financial.SaveGeneralLedgerEntry(revGl);

                    var invoice = _db.SalesInvoiceHeaders.Include(i => i.SalesInvoiceLines).Include(i => i.CustomerAllocations)
                        .First(i => i.CompanyId == companyId && i.Id == wo.SalesInvoiceHeaderId);
                    invoice.WrittenOffAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, invoice.WrittenOffAmount - wo.Amount), precision);
                    invoice.Version++;
                    invoice.RefreshPaymentStatus();

                    wo.Status = SettlementDocumentStatus.Reversed;
                    wo.VoidGeneralLedgerHeaderId = revGl.Id;
                    wo.ReversedBy = _tenant.UserId ?? "system";
                    wo.ReversedAtUtc = DateTime.UtcNow;
                    wo.ReversalReason = request.Reason.Trim();
                    wo.Version++;

                    _db.SaveChanges();
                    return BuildDto(companyId, wo.Id);
                },
                dto => dto.Id,
                nameof(ReceivableWriteOff));
        }

        public ReceivableWriteOffDto RecordRecovery(int writeOffId, RecordWriteOffRecoveryCommand request)
        {
            return _coordinator.Execute(
                "write_off.recover",
                request.IdempotencyKey,
                new { writeOffId, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (request.Amount <= 0 || !CurrencyPrecisionRules.HasValidPrecision(request.Amount, precision))
                        throw new SettlementCommandException(SettlementErrorCodes.Validation, "Recovery amount must be positive.", 400);

                    var wo = _db.ReceivableWriteOffs.FirstOrDefault(w => w.CompanyId == companyId && w.Id == writeOffId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Write-off not found.", 404);
                    if (wo.Status != SettlementDocumentStatus.Posted)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Recoveries can only be recorded against posted write-offs.", 409);
                    if (request.ExpectedWriteOffVersion.HasValue && wo.Version != request.ExpectedWriteOffVersion.Value)
                        throw new SettlementCommandException(SettlementErrorCodes.StaleVersion, "Write-off changed concurrently.", 409);
                    if (request.Date.Date < wo.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Recovery date cannot precede write-off date.", 400);

                    var actor = _tenant.UserId ?? "system";
                    _coordinator.EnsureBankingPickerAccess();
                    var remainingRecoverable = CurrencyPrecisionRules.RoundMoney(wo.Amount - wo.RecoveredAmount, precision);
                    if (request.Amount > remainingRecoverable)
                        throw new SettlementCommandException(SettlementErrorCodes.SourceExhausted, $"Recovery ({request.Amount}) exceeds remaining written-off amount ({remainingRecoverable}).", 409);

                    var bank = _db.Banks.FirstOrDefault(b => b.CompanyId == companyId && b.Id == request.BankId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Bank account not found.", 404);
                    var bankAccId = _mappings.RequireActiveAccount(companyId, bank.AccountId, "Bank GL Account");
                    var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
                    var recoveryAccId = _mappings.RequireActiveAccount(companyId, glSetting?.BadDebtRecoveryAccountId, "Recovery of Written-Off Debts");

                    var no = GenerateNumber("WOR", _db.WriteOffRecoveries.Where(r => r.CompanyId == companyId && r.No != null).Select(r => r.No!));
                    var recovery = new WriteOffRecovery
                    {
                        CompanyId = companyId,
                        ReceivableWriteOffId = wo.Id,
                        No = no,
                        Date = request.Date.Date,
                        Amount = request.Amount,
                        BankId = bank.Id,
                        BankAccountId = bankAccId,
                        BadDebtRecoveryAccountId = recoveryAccId,
                        ReferenceNo = request.ReferenceNo?.Trim(),
                        EvidenceReference = request.EvidenceReference?.Trim(),
                        Status = SettlementDocumentStatus.Posted,
                        PostedBy = _tenant.UserId ?? "system",
                        PostedAtUtc = DateTime.UtcNow,
                        Version = 1
                    };
                    _db.WriteOffRecoveries.Add(recovery);
                    _db.SaveChanges();

                    var glHeader = _financial.CreateGeneralLedgerHeader(
                        DocumentTypes.WriteOffRecovery,
                        recovery.Date,
                        $"Bad-Debt Recovery {no} (Write-Off {wo.No})",
                        recovery.Id);
                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, bankAccId, request.Amount));
                    glHeader.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, recoveryAccId, request.Amount));
                    _financial.SaveGeneralLedgerEntry(glHeader);

                    recovery.GeneralLedgerHeaderId = glHeader.Id;
                    wo.RecoveredAmount = CurrencyPrecisionRules.RoundMoney(wo.RecoveredAmount + request.Amount, precision);
                    wo.Version++;

                    _db.SaveChanges();
                    return BuildDto(companyId, wo.Id);
                },
                dto => dto.Id,
                nameof(ReceivableWriteOff));
        }

        public ReceivableWriteOffDto ReverseRecovery(int writeOffId, int recoveryId, ReverseSettlementRequest request)
        {
            return _coordinator.Execute(
                "write_off_recovery.reverse",
                request.IdempotencyKey,
                new { writeOffId, recoveryId, request },
                (companyId, setting) =>
                {
                    var precision = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
                    if (string.IsNullOrWhiteSpace(request.Reason))
                        throw new SettlementCommandException(SettlementErrorCodes.MissingEvidence, "A reversal reason is required.", 400);

                    var wo = _db.ReceivableWriteOffs.FirstOrDefault(w => w.CompanyId == companyId && w.Id == writeOffId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Write-off not found.", 404);
                    var recovery = _db.WriteOffRecoveries.FirstOrDefault(r => r.CompanyId == companyId && r.Id == recoveryId && r.ReceivableWriteOffId == writeOffId)
                        ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Write-off recovery not found.", 404);
                    if (recovery.Status != SettlementDocumentStatus.Posted || !recovery.GeneralLedgerHeaderId.HasValue)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidState, "Only posted write-off recoveries can be reversed.", 409);
                    if (request.ReversalDate.Date < recovery.Date.Date)
                        throw new SettlementCommandException(SettlementErrorCodes.InvalidDate, "Reversal date cannot precede recovery date.", 400);

                    var revGl = _financial.CreateReversingEntry(
                        recovery.GeneralLedgerHeaderId.Value,
                        $"Reverse Bad-Debt Recovery {recovery.No}: {request.Reason.Trim()}",
                        request.ReversalDate.Date);
                    _financial.SaveGeneralLedgerEntry(revGl);

                    recovery.Status = SettlementDocumentStatus.Reversed;
                    recovery.VoidGeneralLedgerHeaderId = revGl.Id;
                    recovery.ReversedBy = _tenant.UserId ?? "system";
                    recovery.ReversedAtUtc = DateTime.UtcNow;
                    recovery.ReversalReason = request.Reason.Trim();
                    recovery.Version++;

                    wo.RecoveredAmount = CurrencyPrecisionRules.RoundMoney(Math.Max(0m, wo.RecoveredAmount - recovery.Amount), precision);
                    wo.Version++;

                    _db.SaveChanges();
                    return BuildDto(companyId, wo.Id);
                },
                dto => dto.Id,
                nameof(ReceivableWriteOff));
        }

        private ReceivableWriteOffDto BuildDto(int companyId, int id)
        {
            var wo = _db.ReceivableWriteOffs.AsNoTracking()
                .Include(w => w.Customer).ThenInclude(c => c!.Party)
                .Include(w => w.SalesInvoiceHeader)
                .Include(w => w.Recoveries).ThenInclude(r => r.Bank)
                .FirstOrDefault(w => w.CompanyId == companyId && w.Id == id)
                ?? throw new SettlementCommandException(SettlementErrorCodes.NotFound, "Write-off not found.", 404);

            return new ReceivableWriteOffDto(
                wo.Id, wo.No, wo.CustomerId, wo.Customer?.Party?.Name ?? "", wo.SalesInvoiceHeaderId,
                wo.SalesInvoiceHeader?.No ?? "", wo.Date, wo.CurrencyCode, wo.Amount, wo.AllowanceAllocatedAmount,
                wo.ImpairmentShortfallAmount, wo.RecoveredAmount, wo.Reason, wo.CollectionEvidence,
                (int)wo.Status, wo.Status.ToString(), wo.GeneralLedgerHeaderId, wo.VoidGeneralLedgerHeaderId,
                wo.SubmittedBy, wo.ReviewedBy, wo.PostedBy, wo.Version,
                wo.Recoveries.OrderBy(r => r.Date).ThenBy(r => r.Id).Select(r => new WriteOffRecoveryDto(
                    r.Id, r.No, r.Date, r.Amount, r.BankId, r.Bank?.Name ?? "", r.ReferenceNo, r.EvidenceReference,
                    (int)r.Status, r.Status.ToString(), r.GeneralLedgerHeaderId, r.VoidGeneralLedgerHeaderId, r.Version)).ToList());
        }

        private static string GenerateNumber(string prefix, IEnumerable<string> existing)
        {
            var max = 0;
            foreach (var s in existing)
            {
                if (s.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(s[(prefix.Length + 1)..], out var num) && num > max)
                {
                    max = num;
                }
            }
            return $"{prefix}-{(max + 1):D5}";
        }
    }
}
