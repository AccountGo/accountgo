// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Banking;
using Services.Financial;

namespace Services.Banking
{
    public class BankTransactionService : BaseService, IBankTransactionService
    {
        internal const string FeeAccountCode = "50121";
        internal const string InterestIncomeAccountCode = "40300";
        internal const string OpeningBalanceEquityCode = "30700";
        internal const string AccountsReceivableControlCode = "10120";
        internal const string AccountsPayableControlCode = "20110";

        private readonly IRepository<BankTransaction> _transactions;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<Account> _accounts;
        private readonly IRepository<Party> _parties;
        private readonly IRepository<Customer> _customers;
        private readonly IRepository<Vendor> _vendors;
        private readonly IFinancialService _financial;
        private readonly ITenantContext _tenant;

        public BankTransactionService(
            IRepository<SequenceNumber> sequenceNumbers,
            IRepository<GeneralLedgerSetting> glSettings,
            IRepository<PaymentTerm> paymentTerms,
            IRepository<Bank> banks,
            IRepository<BankTransaction> transactions,
            IRepository<Account> accounts,
            IRepository<Party> parties,
            IRepository<Customer> customers,
            IRepository<Vendor> vendors,
            IFinancialService financial,
            ITenantContext tenant)
            : base(sequenceNumbers, glSettings, paymentTerms, banks)
        {
            _transactions = transactions;
            _banks = banks;
            _accounts = accounts;
            _parties = parties;
            _customers = customers;
            _vendors = vendors;
            _financial = financial;
            _tenant = tenant;
        }

        public IReadOnlyList<BankTransactionDto> List(int bankId, DateTime? from, DateTime? to)
        {
            EnsureCompany();
            RequireBank(bankId);
            var fromDate = from?.Date;
            var toDate = to?.Date;
            var query = _transactions.Table.Where(t => t.BankId == bankId || t.ToBankId == bankId);
            if (fromDate.HasValue)
                query = query.Where(t => t.Date >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(t => t.Date <= toDate.Value);
            return query.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList().Select(ToDto).ToList();
        }

        public BankTransactionDto Get(int id)
        {
            EnsureCompany();
            return ToDto(RequireTransaction(id));
        }

        public BankTransactionDto Create(CreateBankTransactionRequest request)
        {
            EnsureCompany();
            if (!Enum.IsDefined(typeof(BankTransactionKinds), request.Kind)
                || request.Kind == (int)BankTransactionKinds.OpeningBalance)
                throw new InvalidOperationException("Pick a valid transaction kind.");

            var kind = (BankTransactionKinds)request.Kind;
            var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0m)
                throw new InvalidOperationException("Amount must be greater than zero.");

            var bank = RequireActiveBank(request.BankId);
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            EnsurePartyInWorkspace(request.PartyId);

            var date = request.Date.Date;
            BankTransaction tx;
            switch (kind)
            {
                case BankTransactionKinds.Transfer:
                    tx = CreateTransfer(bank, request, amount, date);
                    break;
                case BankTransactionKinds.MoneyIn:
                case BankTransactionKinds.Interest:
                    tx = CreateTwoSided(bank, kind, request, amount, date, moneyIn: true);
                    break;
                case BankTransactionKinds.MoneyOut:
                case BankTransactionKinds.Fee:
                    tx = CreateTwoSided(bank, kind, request, amount, date, moneyIn: false);
                    break;
                case BankTransactionKinds.Adjustment:
                    if (!request.IncreaseBalance.HasValue)
                        throw new InvalidOperationException("Pick whether the adjustment increases or decreases the bank balance.");
                    tx = CreateTwoSided(bank, kind, request, amount, date, moneyIn: request.IncreaseBalance.Value);
                    break;
                default:
                    throw new InvalidOperationException("Pick a valid transaction kind.");
            }

            return ToDto(tx);
        }

        public BankTransactionDto Void(int id, VoidBankTransactionRequest request)
        {
            EnsureCompany();
            var tx = RequireTransaction(id);
            if (tx.IsVoid)
                throw new InvalidOperationException("That transaction is already voided.");
            if (tx.Kind == BankTransactionKinds.OpeningBalance)
                throw new InvalidOperationException("Change the opening balance on the bank account instead.");
            if (!tx.GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("That transaction has no ledger entry to reverse.");

            var voidDate = (request.VoidDate ?? DateTime.UtcNow).Date;
            if (voidDate < tx.Date.Date)
                throw new InvalidOperationException("Void date must be on or after the transaction date.");

            var reversal = _financial.CreateReversingEntry(
                tx.GeneralLedgerHeaderId.Value,
                string.IsNullOrWhiteSpace(request.Reason)
                    ? $"Void of bank transaction {tx.No}"
                    : request.Reason!.Trim(),
                voidDate);
            _financial.SaveGeneralLedgerEntry(reversal);
            tx.VoidGeneralLedgerHeaderId = reversal.Id;
            _transactions.Update(tx);
            return ToDto(tx);
        }

        private BankTransaction CreateTransfer(Bank fromBank, CreateBankTransactionRequest request, decimal amount, DateTime date)
        {
            if (!request.ToBankId.HasValue)
                throw new InvalidOperationException("Pick a destination bank account.");
            if (request.ToBankId.Value == fromBank.Id)
                throw new InvalidOperationException("Pick a different destination bank account.");

            var toBank = RequireActiveBank(request.ToBankId.Value);
            if (!toBank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            var description = string.IsNullOrWhiteSpace(request.Memo)
                ? $"Transfer to {toBank.Name}"
                : request.Memo!.Trim();

            var tx = NewTransaction(BankTransactionKinds.Transfer, fromBank.Id, amount, date, request);
            tx.ToBankId = toBank.Id;
            _transactions.Insert(tx);

            var header = _financial.CreateGeneralLedgerHeader(
                DocumentTypes.BankTransaction, date, description, sourceDocumentId: tx.Id);
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, fromBank.AccountId!.Value, amount));
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, toBank.AccountId.Value, amount));
            _financial.SaveGeneralLedgerEntry(header);

            tx.GeneralLedgerHeaderId = header.Id;
            _transactions.Update(tx);
            return tx;
        }

        private BankTransaction CreateTwoSided(
            Bank bank,
            BankTransactionKinds kind,
            CreateBankTransactionRequest request,
            decimal amount,
            DateTime date,
            bool moneyIn)
        {
            var contraId = ResolveContraAccountId(kind, request.ContraAccountId, bank.AccountId!.Value);
            var contra = _accounts.GetById(contraId)
                ?? throw new InvalidOperationException("Pick a postable account.");
            EnsurePostableContra(contra, bank.AccountId.Value);

            var description = string.IsNullOrWhiteSpace(request.Memo)
                ? KindLabel(kind)
                : request.Memo!.Trim();

            var bankSide = moneyIn ? DrOrCrSide.Dr : DrOrCrSide.Cr;
            var contraSide = moneyIn ? DrOrCrSide.Cr : DrOrCrSide.Dr;

            var tx = NewTransaction(kind, bank.Id, amount, date, request);
            tx.ContraAccountId = contraId;
            _transactions.Insert(tx);

            var header = _financial.CreateGeneralLedgerHeader(
                DocumentTypes.BankTransaction, date, description, sourceDocumentId: tx.Id);
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(bankSide, bank.AccountId.Value, amount));
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(contraSide, contraId, amount));
            _financial.SaveGeneralLedgerEntry(header);

            tx.GeneralLedgerHeaderId = header.Id;
            _transactions.Update(tx);
            return tx;
        }

        private int ResolveContraAccountId(BankTransactionKinds kind, int? requested, int bankAccountId)
        {
            if (requested.HasValue)
            {
                if (requested.Value == bankAccountId)
                    throw new InvalidOperationException("Contra account cannot be the same as the bank account.");
                return requested.Value;
            }

            return kind switch
            {
                BankTransactionKinds.Fee => _financial.GetAccountByAccountCode(FeeAccountCode)?.Id
                    ?? throw new InvalidOperationException("Bank Charges (50121) is not set up."),
                BankTransactionKinds.Interest => _financial.GetAccountByAccountCode(InterestIncomeAccountCode)?.Id
                    ?? throw new InvalidOperationException("Interest Income (40300) is not set up."),
                _ => throw new InvalidOperationException("Contra account is required.")
            };
        }

        private void EnsurePostableContra(Account account, int bankAccountId)
        {
            if (account.Id == bankAccountId)
                throw new InvalidOperationException("Contra account cannot be the same as the bank account. Use a Transfer instead.");
            if (account.ArchivedAt.HasValue)
                throw new InvalidOperationException("Pick a postable account.");
            var hasChildren = _accounts.Table.Any(a => a.ParentAccountId == account.Id);
            if (hasChildren || !account.CanPost())
                throw new InvalidOperationException("Pick a postable account.");

            if (string.Equals(account.AccountCode, OpeningBalanceEquityCode, StringComparison.Ordinal))
                throw new InvalidOperationException("Opening Balance Equity can't be used as a contra account.");

            if (string.Equals(account.AccountCode, AccountsReceivableControlCode, StringComparison.Ordinal)
                || string.Equals(account.AccountCode, AccountsPayableControlCode, StringComparison.Ordinal))
                throw new InvalidOperationException("AR and AP control accounts can't be used as a contra account.");

            var glSetting = GetGeneralLedgerSetting();
            if (glSetting?.PayableAccountId == account.Id)
                throw new InvalidOperationException("AR and AP control accounts can't be used as a contra account.");

            if (_customers.Table.Any(c => c.AccountsReceivableAccountId == account.Id)
                || _vendors.Table.Any(v => v.AccountsPayableAccountId == account.Id))
                throw new InvalidOperationException(
                    "Customer AR and vendor AP accounts can't be used as a contra account.");

            if (IsCashOrBankLinked(account))
                throw new InvalidOperationException("Cash and bank accounts can't be used as a contra account. Use a Transfer instead.");
        }

        private bool IsCashOrBankLinked(Account account)
        {
            if (account.IsCash)
                return true;
            if (account.ParentAccountId.HasValue)
            {
                var parent = _accounts.GetById(account.ParentAccountId.Value);
                if (parent != null && parent.IsCash)
                    return true;
            }
            return _banks.Table.Any(b => b.AccountId == account.Id);
        }

        private void EnsurePartyInWorkspace(int? partyId)
        {
            if (!partyId.HasValue)
                return;
            var party = _parties.GetById(partyId.Value);
            if (party == null || party.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(Party), partyId.Value);
        }

        private BankTransaction NewTransaction(
            BankTransactionKinds kind,
            int bankId,
            decimal amount,
            DateTime date,
            CreateBankTransactionRequest request) =>
            new()
            {
                CompanyId = _tenant.RequireCompanyId(),
                Kind = kind,
                BankId = bankId,
                Date = date,
                Amount = amount,
                PartyId = request.PartyId,
                ReferenceNo = string.IsNullOrWhiteSpace(request.ReferenceNo) ? null : request.ReferenceNo.Trim(),
                Memo = string.IsNullOrWhiteSpace(request.Memo) ? null : request.Memo.Trim(),
                No = GetNextNumber(SequenceNumberTypes.BankTransaction).ToString()
            };

        private Bank RequireActiveBank(int bankId)
        {
            var bank = RequireBank(bankId);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("That bank account is archived.");
            return bank;
        }

        private Bank RequireBank(int bankId)
        {
            EnsureCompany();
            var bank = _banks.GetById(bankId);
            if (bank == null || bank.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(Bank), bankId);
            return bank;
        }

        private BankTransaction RequireTransaction(int id)
        {
            var tx = _transactions.GetById(id);
            if (tx == null || tx.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(BankTransaction), id);
            return tx;
        }

        private void EnsureCompany()
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("No company context.");
            _ = _tenant.RequireCompanyId();
        }

        private BankTransactionDto ToDto(BankTransaction tx)
        {
            string? bankName = _banks.GetById(tx.BankId)?.Name;
            string? toBankName = tx.ToBankId.HasValue ? _banks.GetById(tx.ToBankId.Value)?.Name : null;
            string? contraCode = null;
            string? contraName = null;
            if (tx.ContraAccountId.HasValue)
            {
                var contra = _accounts.GetById(tx.ContraAccountId.Value);
                contraCode = contra?.AccountCode;
                contraName = contra?.AccountName;
            }

            string? partyName = null;
            if (tx.PartyId.HasValue)
                partyName = _parties.GetById(tx.PartyId.Value)?.Name;

            return new BankTransactionDto
            {
                Id = tx.Id,
                Kind = (int)tx.Kind,
                KindLabel = KindLabel(tx.Kind),
                BankId = tx.BankId,
                BankName = bankName,
                ToBankId = tx.ToBankId,
                ToBankName = toBankName,
                Date = tx.Date,
                Amount = tx.Amount,
                ContraAccountId = tx.ContraAccountId,
                ContraAccountCode = contraCode,
                ContraAccountName = contraName,
                PartyId = tx.PartyId,
                PartyName = partyName,
                No = tx.No,
                ReferenceNo = tx.ReferenceNo,
                Memo = tx.Memo,
                GeneralLedgerHeaderId = tx.GeneralLedgerHeaderId,
                IsVoid = tx.IsVoid,
                VoidGeneralLedgerHeaderId = tx.VoidGeneralLedgerHeaderId
            };
        }

        internal static string KindLabel(BankTransactionKinds kind) => kind switch
        {
            BankTransactionKinds.MoneyIn => "Money in",
            BankTransactionKinds.MoneyOut => "Money out",
            BankTransactionKinds.Transfer => "Transfer",
            BankTransactionKinds.Fee => "Bank fee",
            BankTransactionKinds.Interest => "Interest",
            BankTransactionKinds.Adjustment => "Adjustment",
            BankTransactionKinds.OpeningBalance => "Opening balance",
            _ => kind.ToString()
        };
    }
}
