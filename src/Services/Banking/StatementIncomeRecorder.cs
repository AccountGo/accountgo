// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Services.Financial;
using Services.Inventory;
using Services.Sales;

namespace Services.Banking
{
    /// <summary>v1: one-line invoice issued and paid on the statement date (#436 I5 decision 2a).</summary>
    public sealed class StatementIncomeRecorder : IStatementIncomeRecorder
    {
        public const string OtherIncomeCustomerName = "Other income";
        public const string GeneralItemName = "Service / Product";

        private static readonly (string Name, string Code)[] CuratedIncomeCodes =
        {
            ("Other income", "40600"),
            ("Interest income", "40300"),
            ("Sales", "40100"),
        };

        private readonly IRepository<BankStatementLine> _lines;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<Account> _accounts;
        private readonly IRepository<Customer> _customers;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly ISalesService _sales;
        private readonly IInventoryService _inventory;
        private readonly IFinancialService _financial;
        private readonly IBankAccountService _bankAccounts;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;

        public StatementIncomeRecorder(
            IRepository<BankStatementLine> lines,
            IRepository<Bank> banks,
            IRepository<Account> accounts,
            IRepository<Customer> customers,
            IRepository<GeneralLedgerLine> glLines,
            ISalesService sales,
            IInventoryService inventory,
            IFinancialService financial,
            IBankAccountService bankAccounts,
            IEntitlementService entitlements,
            ITenantContext tenant)
        {
            _lines = lines;
            _banks = banks;
            _accounts = accounts;
            _customers = customers;
            _glLines = glLines;
            _sales = sales;
            _inventory = inventory;
            _financial = financial;
            _bankAccounts = bankAccounts;
            _entitlements = entitlements;
            _tenant = tenant;
        }

        public StatementIncomeCategoriesDto ListIncomeCategories()
        {
            EnsureCompany();
            var companyId = _tenant.RequireCompanyId();
            var leaves = _accounts.Table
                .Where(a => a.CompanyId == companyId
                    && a.ArchivedAt == null
                    && !a.IsContraAccount
                    && a.AccountClassId == (int)AccountClasses.Revenue
                    && !a.IsCash)
                .ToList()
                .Where(a => !_accounts.Table.Any(c => c.ParentAccountId == a.Id && c.CompanyId == companyId))
                .ToList();

            var curated = new List<StatementIncomeCategoryDto>();
            var used = new HashSet<int>();
            foreach (var (name, code) in CuratedIncomeCodes)
            {
                var account = leaves.FirstOrDefault(a => a.AccountCode == code)
                    ?? leaves.FirstOrDefault(a => a.AccountName.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (account == null || !used.Add(account.Id))
                    continue;
                curated.Add(new StatementIncomeCategoryDto { AccountId = account.Id, Name = name, Curated = true });
            }

            var more = leaves
                .Where(a => !used.Contains(a.Id))
                .OrderBy(a => a.AccountName)
                .Select(a => new StatementIncomeCategoryDto
                {
                    AccountId = a.Id,
                    Name = a.AccountName,
                    Curated = false
                })
                .ToList();

            return new StatementIncomeCategoriesDto { Curated = curated, More = more };
        }

        public RecordOtherIncomeFromStatementLineResultDto Record(
            int lineId,
            RecordOtherIncomeFromStatementLineRequest request,
            string actor)
        {
            EnsureCompany();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var line = RequireUnmatchedDepositLine(lineId);
            var bank = RequireActiveBank(line.BankId);
            var amount = Math.Abs(line.Amount);
            EnsureIncomeAccount(request.IncomeAccountId);

            var customer = ResolveCustomer(request.CustomerId);
            var item = ResolveGeneralItem();
            var description = FirstNonBlank(request.Description, line.Payee, line.Memo) ?? "Other income";

            var measurementId = item.SellMeasurementId ?? item.SmallestMeasurementId
                ?? throw new InvalidOperationException("The catalog item has no unit of measure.");

            // Invoices without an existing order still get a fully-invoiced order (same as SaveSalesInvoice API).
            var salesOrder = new SalesOrderHeader
            {
                CustomerId = customer.Id,
                Date = line.PostedDate.Date,
                ReferenceNo = line.Reference,
                Status = SalesOrderStatus.FullyInvoiced
            };
            var orderLine = new SalesOrderLine
            {
                ItemId = item.Id,
                MeasurementId = measurementId,
                Quantity = 1m,
                Amount = amount
            };
            salesOrder.SalesOrderLines.Add(orderLine);

            var invoiceLine = new SalesInvoiceLine
            {
                ItemId = item.Id,
                MeasurementId = measurementId,
                Quantity = 1m,
                Amount = amount,
                TaxId = request.TaxId,
                Description = description,
                SalesAccountId = request.IncomeAccountId,
                SalesOrderLine = orderLine
            };
            var invoice = new SalesInvoiceHeader
            {
                CustomerId = customer.Id,
                Date = line.PostedDate.Date,
                DueDate = line.PostedDate.Date,
                ReferenceNo = line.Reference,
                Notes = description,
                Status = SalesInvoiceStatus.Open,
                Origin = SalesInvoiceOrigins.StatementOtherIncome
            };
            invoice.SalesInvoiceLines.Add(invoiceLine);

            // Statement other income does not consume the SalesDocuments quota (#436 decision 4).
            using (_entitlements.Bypass())
            {
                _sales.SaveSalesInvoice(invoice, salesOrder);
                _sales.PostSalesInvoice(invoice.Id);
            }

            invoice = _sales.GetSalesInvoiceById(invoice.Id)
                ?? throw new InvalidOperationException("The invoice could not be reloaded after posting.");

            var depositAccountId = _bankAccounts.ResolveDepositAccountId(bank.Id);
            var arAccountId = customer.AccountsReceivableAccountId
                ?? throw new InvalidOperationException("That customer has no accounts receivable account.");

            var receipt = new SalesReceiptHeader
            {
                CustomerId = customer.Id,
                Date = line.PostedDate.Date,
                Amount = amount,
                AccountToDebitId = depositAccountId,
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "Bank Transfer" : request.PaymentMethod.Trim(),
                ReferenceNo = FirstNonBlank(line.Reference, invoice.No),
                Memo = FirstNonBlank(request.Memo, line.Memo, line.Payee)
            };
            receipt.SalesReceiptLines.Add(new SalesReceiptLine
            {
                Quantity = 1,
                Amount = amount,
                AmountPaid = amount,
                AccountToCreditId = arAccountId
            });
            _sales.AddSalesReceipt(receipt);

            var allocation = new CustomerAllocation
            {
                CustomerId = customer.Id,
                Date = line.PostedDate.Date,
                SalesInvoiceHeaderId = invoice.Id,
                SalesReceiptHeaderId = receipt.Id,
                Amount = amount
            };
            invoice.CustomerAllocations.Add(allocation);
            invoice.RefreshPaymentStatus();
            _sales.UpdateSalesInvoice(invoice);

            LinkLineToBankDebit(line, bank, receipt);

            return new RecordOtherIncomeFromStatementLineResultDto
            {
                Line = ToLineDto(line),
                Document = new StatementLineDocumentLinkDto
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

        private Customer ResolveCustomer(int? customerId)
        {
            var companyId = _tenant.RequireCompanyId();
            if (customerId is > 0)
            {
                var existing = _customers.Table.FirstOrDefault(c => c.Id == customerId.Value && c.CompanyId == companyId)
                    ?? throw new TenantEntityNotFoundException(typeof(Customer).Name, customerId.Value);
                if (existing.ArchivedAt != null)
                    throw new InvalidOperationException("That client is archived.");
                return existing;
            }

            var system = _customers.GetAllIncluding(c => c.Party!)
                .FirstOrDefault(c => c.CompanyId == companyId
                    && c.IsSystem
                    && c.Party != null
                    && c.Party.Name == OtherIncomeCustomerName);
            if (system != null)
                return system;

            // Mirror the defaults SalesController uses when creating a client.
            var accounts = _financial.GetAccounts().ToList();
            var arId = accounts.FirstOrDefault(a => a.AccountCode == "10120")?.Id
                ?? throw new InvalidOperationException("The workspace has no Accounts Receivable account (10120).");
            var salesId = accounts.FirstOrDefault(a => a.AccountCode == "40100")?.Id
                ?? accounts.FirstOrDefault(a => a.AccountClassId == (int)AccountClasses.Revenue)?.Id;

            var customer = new Customer
            {
                IsSystem = true,
                AccountsReceivableAccountId = arId,
                SalesAccountId = salesId,
                Party = new Party
                {
                    PartyType = PartyTypes.Customer,
                    IsActive = true,
                    Name = OtherIncomeCustomerName
                },
                PrimaryContact = new Contact
                {
                    ContactType = ContactTypes.Customer,
                    FirstName = "Other",
                    LastName = "income",
                    Party = new Party
                    {
                        PartyType = PartyTypes.Contact,
                        Name = OtherIncomeCustomerName
                    }
                }
            };
            _sales.AddCustomer(customer);
            return customer;
        }

        private Item ResolveGeneralItem()
        {
            var items = _inventory.GetAllItems().ToList();
            var item = items.FirstOrDefault(i =>
                i.ArchivedAt == null
                && string.Equals(i.Description, GeneralItemName, StringComparison.OrdinalIgnoreCase));
            if (item != null)
                return item;

            var measurements = _inventory.GetMeasurements().ToList();
            var measurement = measurements.FirstOrDefault(m => m.Code == "EA") ?? measurements.FirstOrDefault()
                ?? throw new InvalidOperationException("No unit of measure is set up for this workspace.");
            var category = _inventory.GetItemCategories().FirstOrDefault(c => c.ItemType == ItemTypes.Service);
            if (category == null)
            {
                category = new ItemCategory
                {
                    Name = "Services",
                    ItemType = ItemTypes.Service,
                    MeasurementId = measurement.Id,
                    SalesAccountId = _financial.GetAccounts().FirstOrDefault(a => a.AccountCode == "40100")?.Id
                };
                _inventory.SaveItemCategory(category);
            }

            item = new Item
            {
                ItemCategoryId = category.Id,
                Description = GeneralItemName,
                SellDescription = GeneralItemName,
                SmallestMeasurementId = measurement.Id,
                SellMeasurementId = measurement.Id,
                SalesAccountId = category.SalesAccountId
            };
            _inventory.AddItem(item);
            return item;
        }

        private void EnsureIncomeAccount(int accountId)
        {
            if (accountId <= 0)
                throw new InvalidOperationException("Pick an income category.");
            var companyId = _tenant.RequireCompanyId();
            var account = _accounts.Table.FirstOrDefault(a => a.Id == accountId && a.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(Account).Name, accountId);
            if (account.AccountClassId != (int)AccountClasses.Revenue || account.IsContraAccount)
                throw new InvalidOperationException("Pick an income category.");
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
                throw new InvalidOperationException("Record other income applies to money-in lines only.");
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
