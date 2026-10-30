// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Banking;
using Services.Sales;

namespace Services.Banking
{
    public sealed class StatementLineInvoiceService : IStatementLineInvoiceService
    {
        private readonly IRepository<BankStatementLine> _lines;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly ISalesService _sales;
        private readonly IBankAccountService _bankAccounts;
        private readonly IInvoicePaymentRecorder _invoicePayments;
        private readonly ITenantContext _tenant;

        public StatementLineInvoiceService(
            IRepository<BankStatementLine> lines,
            IRepository<Bank> banks,
            IRepository<GeneralLedgerLine> glLines,
            ISalesService sales,
            IBankAccountService bankAccounts,
            IInvoicePaymentRecorder invoicePayments,
            ITenantContext tenant)
        {
            _lines = lines;
            _banks = banks;
            _glLines = glLines;
            _sales = sales;
            _bankAccounts = bankAccounts;
            _invoicePayments = invoicePayments;
            _tenant = tenant;
        }

        public ApplyOpenInvoiceFromStatementLineResultDto ApplyOpenInvoice(
            int lineId,
            ApplyOpenInvoiceFromStatementLineRequest request,
            string actor)
        {
            EnsureCompany();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var line = RequireUnmatchedDepositLine(lineId);
            var bank = RequireActiveBank(line.BankId);
            var amount = Math.Abs(line.Amount);
            var companyId = _tenant.RequireCompanyId();

            var invoice = _sales.GetSalesInvoiceById(request.InvoiceId);
            if (invoice == null || invoice.CompanyId != companyId)
                throw new TenantEntityNotFoundException(typeof(SalesInvoiceHeader).Name, request.InvoiceId);

            if (!invoice.GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("Issue the invoice before applying a statement payment.");

            invoice.EnsureCanRecordPayment(amount);

            if (line.PostedDate.Date < invoice.Date.Date)
                throw new InvalidOperationException(
                    $"The statement date ({line.PostedDate:yyyy-MM-dd}) cannot be earlier than the invoice date ({invoice.Date:yyyy-MM-dd}).");

            var depositAccountId = _bankAccounts.ResolveDepositAccountId(bank.Id);
            var arAccountId = invoice.Customer?.AccountsReceivableAccountId
                ?? throw new InvalidOperationException("That invoice's customer has no accounts receivable account.");

            var receipt = _invoicePayments.RecordAllocatedReceipt(
                invoice,
                amount,
                line.PostedDate.Date,
                depositAccountId,
                arAccountId,
                request.PaymentMethod,
                FirstNonBlank(line.Reference, invoice.No),
                FirstNonBlank(request.Memo, line.Memo, line.Payee));
            _sales.UpdateSalesInvoice(invoice);

            LinkLineToBankDebit(line, bank, receipt);

            return new ApplyOpenInvoiceFromStatementLineResultDto
            {
                Line = ToLineDto(line),
                Invoice = new StatementLineDocumentLinkDto
                {
                    Kind = "invoice",
                    Id = invoice.Id,
                    No = invoice.No
                },
                Receipt = new StatementLineDocumentLinkDto
                {
                    Kind = "salesReceipt",
                    Id = receipt.Id,
                    No = receipt.No
                }
            };
        }

        private void LinkLineToBankDebit(BankStatementLine line, Bank bank, SalesReceiptHeader receipt)
        {
            var headerId = receipt.GeneralLedgerHeaderId ?? receipt.GeneralLedgerHeader?.Id;
            if (!headerId.HasValue || !bank.AccountId.HasValue)
                throw new InvalidOperationException("The receipt did not post a bank ledger line.");

            var bankAccountId = bank.AccountId.Value;
            var glLine = _glLines.Table.FirstOrDefault(l =>
                    l.GeneralLedgerHeaderId == headerId.Value
                    && l.AccountId == bankAccountId
                    && l.DrCr == DrOrCrSide.Dr)
                ?? throw new InvalidOperationException("Could not find the bank debit for the receipt.");

            line.MatchedGeneralLedgerLineId = glLine.Id;
            _lines.Update(line);
        }

        private BankStatementLine RequireUnmatchedDepositLine(int lineId)
        {
            var companyId = _tenant.RequireCompanyId();
            var line = _lines.Table.FirstOrDefault(l => l.Id == lineId && l.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(BankStatementLine).Name, lineId);
            if (line.ExcludedAt.HasValue)
                throw new InvalidOperationException("That statement line is excluded. Include it first.");
            if (line.MatchedGeneralLedgerLineId.HasValue)
                throw new InvalidOperationException("That statement line is already matched.");
            if (line.Amount <= 0m)
                throw new InvalidOperationException("Apply invoice applies to money-in lines only.");
            return line;
        }

        private Bank RequireActiveBank(int bankId)
        {
            var companyId = _tenant.RequireCompanyId();
            var bank = _banks.Table.FirstOrDefault(b => b.Id == bankId && b.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(Bank).Name, bankId);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("That bank account is archived.");
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");
            return bank;
        }

        private void EnsureCompany()
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("No active workspace.");
            _ = _tenant.RequireCompanyId();
        }

        private static BankStatementLineDto ToLineDto(BankStatementLine line) => new()
        {
            Id = line.Id,
            BankId = line.BankId,
            BankStatementImportId = line.BankStatementImportId,
            Source = (int)line.Source,
            ExternalId = line.ExternalId,
            PostedDate = line.PostedDate,
            Amount = line.Amount,
            Payee = line.Payee,
            Memo = line.Memo,
            Reference = line.Reference,
            MatchedGeneralLedgerLineId = line.MatchedGeneralLedgerLineId,
            IsMatched = line.MatchedGeneralLedgerLineId.HasValue,
            IsExcluded = line.ExcludedAt.HasValue,
            ExcludedAt = line.ExcludedAt,
            ExcludedBy = line.ExcludedBy,
            ExclusionReason = line.ExclusionReason.HasValue ? (int)line.ExclusionReason.Value : null,
            ExclusionNote = line.ExclusionNote,
            Candidates = Array.Empty<BankStatementMatchCandidateDto>()
        };

        private static string? FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return null;
        }
    }
}
