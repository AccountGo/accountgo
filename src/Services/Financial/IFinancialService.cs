//-----------------------------------------------------------------------
// <copyright file="IFinancialService.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.TaxSystem;
using System;
using System.Collections.Generic;

namespace Services.Financial
{
    public partial interface IFinancialService
    {
        bool ValidateGeneralLedgerEntry(GeneralLedgerHeader glEntry);
        void SaveGeneralLedgerEntry(GeneralLedgerHeader entry);
        void UpdateGeneralLedgerHeader(GeneralLedgerHeader entry);
        GeneralLedgerLine CreateGeneralLedgerLine(DrOrCrSide DrCr, int accountId, decimal amount);
        GeneralLedgerHeader CreateGeneralLedgerHeader(DocumentTypes documentType, DateTime Date, string description, int? sourceDocumentId = null, int? reversedGeneralLedgerHeaderId = null);
        IEnumerable<Account> GetAccounts();
        IEnumerable<JournalEntryHeader> GetJournalEntries();
        IEnumerable<JournalEntryLine> GetJournalEntryLines();
        void AddJournalEntry(JournalEntryHeader journalEntry);
        /// <summary>
        /// Allows create/update/post of <see cref="JournalVoucherTypes.ClosingEntries"/> journals
        /// (used by year-end close/reopen). Outside this scope the voucher type is rejected.
        /// </summary>
        IDisposable AllowClosingEntries();
        ICollection<TrialBalance> TrialBalance(DateTime? from = null, DateTime? to = null, AccountingBasis basis = AccountingBasis.Accrual);
        ICollection<BalanceSheet> BalanceSheet(DateTime? from = null, DateTime? to = null, AccountingBasis basis = AccountingBasis.Accrual);
        ICollection<IncomeStatement> IncomeStatement(DateTime? from = null, DateTime? to = null, AccountingBasis basis = AccountingBasis.Accrual);
        CashFlowStatement CashFlow(DateTime? from = null, DateTime? to = null, AccountingBasis basis = AccountingBasis.Accrual);
        ICollection<MasterGeneralLedger> MasterGeneralLedger(DateTime? from = null, DateTime? to = null, string accountCode = null, int? transactionNo = null);
        /// <summary>Lists tenant-scoped posted journals, retaining complete entries for account filters.</summary>
        /// <param name="from">Inclusive posting date start.</param>
        /// <param name="to">Inclusive posting date end (date-only values expand to end of calendar day).</param>
        /// <param name="accountCode">Optional account touched by the journal.</param>
        /// <param name="transactionNo">Optional exact posted header identifier.</param>
        /// <param name="documentType">Optional source document type.</param>
        /// <param name="skip">Headers to skip after filtering (default 0).</param>
        /// <param name="take">Page size (default 100, maximum 500).</param>
        /// <returns>Paged posted journals ordered by date and identifier descending.</returns>
        Dto.Financial.GeneralLedgerTransactionPage GetGeneralLedgerTransactions(
            DateTime? from = null, DateTime? to = null, string accountCode = null, int? transactionNo = null,
            DocumentTypes? documentType = null, int skip = 0, int take = 100);
        FinancialYear CurrentFiscalYear();
        /// <summary>
        /// Creates every missing open fiscal year from the books start through the year containing <paramref name="date"/>.
        /// Idempotent: never duplicates or mutates existing years. No-op when <paramref name="date"/> is before the books start.
        /// </summary>
        void EnsureFiscalYearsThrough(DateTime date);
        IEnumerable<Tax> GetTaxes();
        IEnumerable<ItemTaxGroup> GetItemTaxGroups();
        IEnumerable<TaxGroup> GetTaxGroups();
        IEnumerable<Bank> GetCashAndBanks();
        List<KeyValuePair<int, decimal>> ComputeInputTax(int vendorId, int itemId, decimal quantity, decimal amount, decimal discount);
        List<KeyValuePair<int, decimal>> ComputeOutputTax(int customerId, int itemId, decimal quantity, decimal amount, decimal discount);
        GeneralLedgerSetting GetGeneralLedgerSetting();
        void SaveGeneralLedgerSetting(GeneralLedgerSetting setting);
        void AddMainContraAccountSetting(int masterAccountId, int contraAccountId);
        void UpdateAccount(Account account);
        JournalEntryHeader GetJournalEntry(int id, bool fromGL = false);
        void UpdateJournalEntry(JournalEntryHeader journalEntry, bool posted = false);
        GeneralLedgerHeader GetGeneralLedgerHeader(int id);
        GeneralLedgerHeader CreateReversingEntry(int generalLedgerHeaderId, string description, DateTime reversalDate);
        /// <summary>Throws <see cref="Services.Banking.ReconciledTransactionException"/> when any line on the header is stamped by a finished reconciliation.</summary>
        void EnsureNotReconciled(int generalLedgerHeaderId);
        IEnumerable<GeneralLedgerLine> GetGeneralLedgerLines(int generalLedgerHeaderId);
        Account GetAccountByAccountCode(string accountcode);
        Account GetAccount(int id);
        void AddAccount(Account account);
        void SaveAccountClasses(IList<AccountClass> accountClasses);
        void SaveFinancialYear(FinancialYear financialYear);
        void SavePaymentTerm(PaymentTerm paymentTerm);
        void SaveBank(Bank bank);
        Bank GetBank(int id);
        void DeleteBank(int id);
    }
}
