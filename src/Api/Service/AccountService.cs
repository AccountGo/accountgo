using Api.Data;
using Api.Service.Import;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Microsoft.EntityFrameworkCore;
using Services.Banking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class AccountService : IAccountService
{
    private readonly ApiDbContext _context;
    private readonly ICoaFormatAdapterRegistry _formatAdapters;
    private readonly IBankAccountService _bankAccounts;

    public AccountService(ApiDbContext context, ICoaFormatAdapterRegistry formatAdapters, IBankAccountService bankAccounts)
    {
        _context = context;
        _formatAdapters = formatAdapters ?? throw new ArgumentNullException(nameof(formatAdapters));
        _bankAccounts = bankAccounts ?? throw new ArgumentNullException(nameof(bankAccounts));
    }

    // Add a new account
    public async Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount)
    {
        // Set default values for required fields if not already set
        if (newAccount.ParentAccountId.HasValue)
        {
            var parent = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == newAccount.ParentAccountId.Value);
            if (parent == null)
            {
                throw new InvalidOperationException($"Parent account #{newAccount.ParentAccountId.Value} not found.");
            }

            // #413 D2: a bank posting account cannot become a summary by gaining a child.
            _bankAccounts.EnsureAccountMayChangeWhileBackingBank(
                parent,
                proposedParent: null,
                proposedCode: parent.AccountCode,
                becomingSummary: true,
                archiving: false);

            if (newAccount.AccountClassId == 0)
            {
                newAccount.AccountClassId = parent.AccountClassId;
            }
        }

        if (newAccount.AccountClassId == 0)
        {
            var firstAccountClass = await _context.Set<Core.Domain.Financials.AccountClass>()
                .FirstOrDefaultAsync();

            if (firstAccountClass != null)
            {
                newAccount.AccountClassId = firstAccountClass.Id;
            }
            else
            {
                throw new InvalidOperationException("No AccountClass found in the database. Please ensure the database is properly initialized.");
            }
        }

        // Set default DrOrCrSide if not set (default to Dr)
        if (newAccount.DrOrCrSide == Core.Domain.DrOrCrSide.NA)
        {
            newAccount.DrOrCrSide = Core.Domain.DrOrCrSide.Dr;
        }

        // Check for duplicate account code
        var existingAccount = await _context.Accounts
            .AnyAsync(a => a.AccountCode == newAccount.AccountCode);
        if (existingAccount)
        {
            throw new InvalidOperationException($"Account number '{newAccount.AccountCode}' already exists.");
        }

        _context.Accounts.Add(newAccount);
        await _context.SaveChangesAsync();
        return newAccount;
    }

    // Update an existing account
    public async Task<Core.Domain.Financials.Account?> UpdateAccountAsync(string accountCode, Core.Domain.Financials.Account updatedAccount)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return null;

        // Code update
        if (!string.IsNullOrWhiteSpace(updatedAccount.AccountCode) &&
            !string.Equals(updatedAccount.AccountCode, account.AccountCode, StringComparison.OrdinalIgnoreCase))
        {
            var codeInUse = await _context.Accounts
                .AnyAsync(a => a.AccountCode == updatedAccount.AccountCode && a.Id != account.Id);
            if (codeInUse)
                throw new InvalidOperationException($"Account number '{updatedAccount.AccountCode}' already exists.");

            account.AccountCode = updatedAccount.AccountCode;
        }

        // Parent update & cycle detection
        if (updatedAccount.ParentAccountId != account.ParentAccountId)
        {
            if (updatedAccount.ParentAccountId.HasValue)
            {
                var targetParentId = updatedAccount.ParentAccountId.Value;
                if (targetParentId == account.Id)
                    throw new InvalidOperationException("An account cannot be its own parent.");

                var current = (int?)targetParentId;
                var visited = new HashSet<int> { account.Id };
                while (current.HasValue)
                {
                    if (visited.Contains(current.Value))
                        throw new InvalidOperationException("Cannot reparent account: circular hierarchy detected.");
                    visited.Add(current.Value);
                    current = await _context.Accounts
                        .Where(a => a.Id == current.Value)
                        .Select(a => a.ParentAccountId)
                        .FirstOrDefaultAsync();
                }
            }

            account.ParentAccountId = updatedAccount.ParentAccountId;
        }

        // Class change check (prohibited if account has transaction history)
        if (updatedAccount.AccountClassId != 0 && updatedAccount.AccountClassId != account.AccountClassId)
        {
            var hasHistory = await _context.GeneralLedgerLines.AnyAsync(gll => gll.AccountId == account.Id)
                || await _context.JournalEntryLines.AnyAsync(jel => jel.AccountId == account.Id);
            if (hasHistory)
                throw new InvalidOperationException("Cannot change account class because this account has transaction history.");

            account.AccountClassId = updatedAccount.AccountClassId;
        }

        if (updatedAccount.AccountName != null) account.AccountName = updatedAccount.AccountName;
        if (updatedAccount.Description != null) account.Description = updatedAccount.Description;
        account.IsCash = updatedAccount.IsCash;
        account.IsContraAccount = updatedAccount.IsContraAccount;

        Account? proposedParent = null;
        if (account.ParentAccountId.HasValue)
            proposedParent = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == account.ParentAccountId.Value);

        _bankAccounts.EnsureAccountMayChangeWhileBackingBank(
            account,
            proposedParent,
            account.AccountCode,
            becomingSummary: false,
            archiving: false);

        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();

        // D8: renaming a bank-backed account renames the bank (unique-name check inside).
        _bankAccounts.SyncBankNameFromAccount(account);

        return account;
    }

    public async Task<(bool isProtected, string? protectionReason, bool canDelete, string? deleteBlockedReason)> GetAccountProtectionInfoAsync(int accountId)
    {
        // 1. Protected Check (§6.1: GLS, Tax, Bank, Children)
        // Check children
        var hasChildren = await _context.Accounts.AnyAsync(a => a.ParentAccountId == accountId);
        if (hasChildren)
        {
            return (true, "Account has child accounts. Reassign or delete child accounts first.",
                    false, "Cannot delete account because it has child accounts. Reassign or delete child accounts first.");
        }

        // Check GeneralLedgerSetting
        var gls = await _context.GeneralLedgerSettings.FirstOrDefaultAsync(s =>
            s.PayableAccountId == accountId ||
            s.PurchaseDiscountAccountId == accountId ||
            s.GoodsReceiptNoteClearingAccountId == accountId ||
            s.SalesDiscountAccountId == accountId ||
            s.ShippingChargeAccountId == accountId ||
            s.PermanentAccountId == accountId ||
            s.IncomeSummaryAccountId == accountId);

        if (gls != null)
        {
            string role = gls.PayableAccountId == accountId ? "Payable Account" :
                          gls.PurchaseDiscountAccountId == accountId ? "Purchase Discount Account" :
                          gls.GoodsReceiptNoteClearingAccountId == accountId ? "Goods Received Note Clearing Account" :
                          gls.SalesDiscountAccountId == accountId ? "Sales Discount Account" :
                          gls.ShippingChargeAccountId == accountId ? "Shipping Charge Account" :
                          gls.PermanentAccountId == accountId ? "Permanent (Retained Earnings) Account" :
                          "Income Summary Account";
            var msg = $"Account is mapped in General Ledger Settings as {role}. Remap in Settings first.";
            return (true, msg, false, msg);
        }

        // Check Tax links
        var tax = await _context.Taxes.FirstOrDefaultAsync(t => t.SalesAccountId == accountId || t.PurchasingAccountId == accountId);
        if (tax != null)
        {
            var msg = $"Account is mapped in Tax Settings for tax rate '{tax.TaxName}'. Remap in Tax Settings first.";
            return (true, msg, false, msg);
        }

        // Check Bank links
        var bank = await _context.Banks.FirstOrDefaultAsync(b => b.AccountId == accountId);
        if (bank != null)
        {
            var msg = $"Account is linked as GL account for Bank '{bank.Name}'. Remap bank account in Banking first.";
            return (true, msg, false, msg);
        }

        // Not protected! Now check Delete eligibility (§5.1, §5.2)
        // Transaction history
        if (await _context.GeneralLedgerLines.AnyAsync(gll => gll.AccountId == accountId))
            return (false, null, false, "Cannot delete account because it has transaction history in the general ledger. Use Archive instead.");

        if (await _context.JournalEntryLines.AnyAsync(jel => jel.AccountId == accountId))
            return (false, null, false, "Cannot delete account because it has transaction history in journal entries. Use Archive instead.");

        if (await _context.SalesReceiptHeaders.AnyAsync(srh => srh.AccountToDebitId == accountId) ||
            await _context.SalesReceiptLines.AnyAsync(srl => srl.AccountToCreditId == accountId))
            return (false, null, false, "Cannot delete account because it has transaction history in sales receipts. Use Archive instead.");

        if (await _context.SalesInvoiceLines.AnyAsync(sil => sil.SalesAccountId == accountId))
            return (false, null, false, "Cannot delete account because it is referenced in sales invoices. Use Archive instead.");

        if (await _context.PurchaseInvoiceLines.AnyAsync(pil => pil.AccountId == accountId))
            return (false, null, false, "Cannot delete account because it is referenced in purchase bills. Use Archive instead.");

        if (await _context.VendorPayments.AnyAsync(vp => vp.AccountId == accountId))
            return (false, null, false, "Cannot delete account because it is referenced in vendor payments. Use Archive instead.");

        // Item & category links
        if (await _context.ItemCategories.AnyAsync(ic => ic.AdjustmentAccountId == accountId ||
                                                         ic.AssemblyAccountId == accountId ||
                                                         ic.CostOfGoodsSoldAccountId == accountId ||
                                                         ic.InventoryAccountId == accountId ||
                                                         ic.SalesAccountId == accountId))
            return (false, null, false, "Cannot delete account because it is used by one or more item categories. Reassign item categories first.");

        if (await _context.Items.AnyAsync(i => i.InventoryAdjustmentAccountId == accountId ||
                                               i.CostOfGoodsSoldAccountId == accountId ||
                                               i.InventoryAccountId == accountId ||
                                               i.SalesAccountId == accountId))
            return (false, null, false, "Cannot delete account because it is used by one or more items. Reassign items first.");

        // Party links (no silent nulling!)
        if (await _context.Customers.AnyAsync(c => c.AccountsReceivableAccountId == accountId ||
                                                   c.CustomerAdvancesAccountId == accountId ||
                                                   c.PromptPaymentDiscountAccountId == accountId ||
                                                   c.SalesAccountId == accountId ||
                                                   c.SalesDiscountAccountId == accountId))
            return (false, null, false, "Cannot delete account because it is configured on one or more customers. Unmap from customers first.");

        if (await _context.Vendors.AnyAsync(v => v.AccountsPayableAccountId == accountId ||
                                                 v.PurchaseAccountId == accountId ||
                                                 v.PurchaseDiscountAccountId == accountId))
            return (false, null, false, "Cannot delete account because it is configured on one or more vendors. Unmap from vendors first.");

        // Contra links
        if (await _context.MainContraAccounts.AnyAsync(mca => mca.MainAccountId == accountId || mca.RelatedContraAccountId == accountId))
            return (false, null, false, "Cannot delete account because it is linked in contra accounts. Remove the contra link first.");

        return (false, null, true, null);
    }

    // Archive an account
    public async Task<(Core.Domain.Financials.Account? account, string? errorMessage)> ArchiveAccountAsync(string accountCode)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return (null, "Account not found");

        if (account.ArchivedAt.HasValue)
            return (account, null); // Already archived

        var (isProtected, protectionReason, _, _) = await GetAccountProtectionInfoAsync(account.Id);
        if (isProtected)
        {
            return (null, $"Cannot archive account: {protectionReason}");
        }

        try
        {
            _bankAccounts.EnsureAccountMayChangeWhileBackingBank(
                account,
                proposedParent: null,
                proposedCode: account.AccountCode,
                becomingSummary: false,
                archiving: true);
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }

        account.ArchivedAt = DateTime.UtcNow;
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();

        return (account, null);
    }

    // Unarchive an account
    public async Task<(Core.Domain.Financials.Account? account, string? errorMessage)> UnarchiveAccountAsync(string accountCode)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return (null, "Account not found");

        if (!account.ArchivedAt.HasValue)
            return (account, null); // Already active

        account.ArchivedAt = null;
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();

        return (account, null);
    }

    // Delete an account (Never silently nulls control or party FKs!)
    public async Task<(Core.Domain.Financials.Account? account, string? errorMessage)> DeleteAccountAsync(string accountCode)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return (null, "Account not found");

        var (_, _, canDelete, deleteBlockedReason) = await GetAccountProtectionInfoAsync(account.Id);
        if (!canDelete)
        {
            return (null, deleteBlockedReason);
        }

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();
        return (account, null);
    }

    // Get an account by AccountCode
    public async Task<Core.Domain.Financials.Account?> GetAccountByCodeAsync(string accountCode)
    {
        return await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
    }

    // List all accounts
    public async Task<IEnumerable<Core.Domain.Financials.Account>> GetAllAccountsAsync()
    {
        return await _context.Accounts.ToListAsync();
    }

    public async Task<Dto.Financial.ChartOfAccountsStatusDto> GetChartStatusAsync()
    {
        // 1. Only posted journals block Replace (draft journals do not block Replace)
        int postedJeCount = await _context.JournalEntryHeaders.CountAsync(j => j.Posted == true || j.GeneralLedgerHeaderId.HasValue);
        int gllCount = await _context.GeneralLedgerLines.CountAsync();
        int silCount = await _context.SalesInvoiceLines.CountAsync();
        int pilCount = await _context.PurchaseInvoiceLines.CountAsync();
        int srhCount = await _context.SalesReceiptHeaders.CountAsync();
        int vpCount = await _context.VendorPayments.CountAsync();

        int totalPosted = postedJeCount + gllCount + silCount + pilCount + srhCount + vpCount;
        bool isEmpty = totalPosted == 0;

        var controls = await GetRequiredControlRolesAsync();

        string? blockedReason = null;
        if (!isEmpty)
        {
            var parts = new List<string>();
            if (postedJeCount > 0) parts.Add($"{postedJeCount} posted journal(s)");
            if (gllCount > 0) parts.Add($"{gllCount} general ledger line(s)");
            if (silCount > 0) parts.Add($"{silCount} sales invoice line(s)");
            if (pilCount > 0) parts.Add($"{pilCount} purchase invoice line(s)");
            if (srhCount > 0) parts.Add($"{srhCount} sales receipt(s)");
            if (vpCount > 0) parts.Add($"{vpCount} vendor payment(s)");
            blockedReason = $"Replace mode is disabled because transactions have been posted to this workspace ({string.Join(", ", parts)}).";
        }

        return new Dto.Financial.ChartOfAccountsStatusDto
        {
            IsEmptyBooks = isEmpty,
            PostedTransactionCount = totalPosted,
            ReplaceBlockedReason = blockedReason,
            CanReplace = isEmpty,
            RequiredControlRoles = controls
        };
    }

    public async Task<List<Dto.Financial.ControlRoleMappingDto>> GetRequiredControlRolesAsync()
    {
        var list = new List<Dto.Financial.ControlRoleMappingDto>();
        var accounts = await _context.Accounts.ToListAsync();
        var accountsById = accounts.ToDictionary(a => a.Id);

        // GeneralLedgerSetting
        var gls = await _context.GeneralLedgerSettings.FirstOrDefaultAsync();
        if (gls != null)
        {
            AddGlsRole(list, "PayableAccount", "Accounts Payable (GLS)", gls.PayableAccountId, accountsById);
            AddGlsRole(list, "PurchaseDiscountAccount", "Purchase Discount (GLS)", gls.PurchaseDiscountAccountId, accountsById);
            AddGlsRole(list, "GoodsReceiptNoteClearingAccount", "Goods Receipt Note Clearing (GLS)", gls.GoodsReceiptNoteClearingAccountId, accountsById);
            AddGlsRole(list, "SalesDiscountAccount", "Sales Discount (GLS)", gls.SalesDiscountAccountId, accountsById);
            AddGlsRole(list, "ShippingChargeAccount", "Shipping Charge (GLS)", gls.ShippingChargeAccountId, accountsById);
            AddGlsRole(list, "PermanentAccount", "Retained Earnings / Permanent (GLS)", gls.PermanentAccountId, accountsById);
            AddGlsRole(list, "IncomeSummaryAccount", "Income Summary (GLS)", gls.IncomeSummaryAccountId, accountsById);
        }

        // Taxes - all taxes with accounts (both active and inactive)
        var taxes = await _context.Taxes.ToListAsync();
        foreach (var tax in taxes)
        {
            if (tax.SalesAccountId.HasValue)
            {
                var acct = accountsById.GetValueOrDefault(tax.SalesAccountId.Value);
                list.Add(new Dto.Financial.ControlRoleMappingDto
                {
                    RoleKey = $"Tax_Sales_{tax.Id}",
                    RoleName = $"Tax Sales Account ({tax.TaxName ?? tax.TaxCode})",
                    SourceType = "Tax",
                    SourceEntityId = tax.Id,
                    CurrentAccountId = tax.SalesAccountId,
                    CurrentAccountCode = acct?.AccountCode,
                    CurrentAccountName = acct?.AccountName,
                    IsRequired = true
                });
            }
            if (tax.PurchasingAccountId.HasValue)
            {
                var acct = accountsById.GetValueOrDefault(tax.PurchasingAccountId.Value);
                list.Add(new Dto.Financial.ControlRoleMappingDto
                {
                    RoleKey = $"Tax_Purchase_{tax.Id}",
                    RoleName = $"Tax Purchase Account ({tax.TaxName ?? tax.TaxCode})",
                    SourceType = "Tax",
                    SourceEntityId = tax.Id,
                    CurrentAccountId = tax.PurchasingAccountId,
                    CurrentAccountCode = acct?.AccountCode,
                    CurrentAccountName = acct?.AccountName,
                    IsRequired = true
                });
            }
        }

        // Banks - all banks with accounts (both active and inactive)
        var banks = await _context.Banks.ToListAsync();
        foreach (var bank in banks)
        {
            if (bank.AccountId.HasValue)
            {
                var acct = accountsById.GetValueOrDefault(bank.AccountId.Value);
                list.Add(new Dto.Financial.ControlRoleMappingDto
                {
                    RoleKey = $"Bank_{bank.Id}",
                    RoleName = $"Bank GL Account ({bank.Name})",
                    SourceType = "Bank",
                    SourceEntityId = bank.Id,
                    CurrentAccountId = bank.AccountId,
                    CurrentAccountCode = acct?.AccountCode,
                    CurrentAccountName = acct?.AccountName,
                    IsRequired = true
                });
            }
        }

        return list;
    }

    private static void AddGlsRole(
        List<Dto.Financial.ControlRoleMappingDto> list,
        string roleKey,
        string roleName,
        int? currentAccountId,
        Dictionary<int, Core.Domain.Financials.Account> accountsById)
    {
        var acct = currentAccountId.HasValue ? accountsById.GetValueOrDefault(currentAccountId.Value) : null;
        list.Add(new Dto.Financial.ControlRoleMappingDto
        {
            RoleKey = roleKey,
            RoleName = roleName,
            SourceType = "GeneralLedgerSetting",
            CurrentAccountId = currentAccountId,
            CurrentAccountCode = acct?.AccountCode,
            CurrentAccountName = acct?.AccountName,
            IsRequired = true
        });
    }

    public async Task<Dto.Financial.CoaImportPreviewDto> ValidateImportAsync(
        string csvContent,
        string mode,
        bool retireMissing = false,
        Dictionary<string, string>? controlMappings = null,
        bool isCallerOwner = false)
    {
        string normalizedMode = (mode ?? "append").Trim().ToLowerInvariant();
        bool isAppend = normalizedMode == "append";
        bool isUpsert = normalizedMode == "upsert";
        bool isReplace = normalizedMode == "replace";

        var preview = new Dto.Financial.CoaImportPreviewDto
        {
            Mode = normalizedMode
        };

        if (!isAppend && !isUpsert && !isReplace)
        {
            preview.FileErrors.Add($"Unsupported import mode '{mode}'. Expected 'append', 'upsert', or 'replace'.");
            return preview;
        }

        // Owner gate check (§9, §11 D9)
        if (isReplace && !isCallerOwner)
        {
            preview.FileErrors.Add("Replace mode requires workspace Owner role.");
        }
        if (retireMissing && !isCallerOwner)
        {
            preview.FileErrors.Add("Retire Missing option requires workspace Owner role.");
        }
        if (retireMissing && !isUpsert)
        {
            preview.FileWarnings.Add("Retire Missing option is only applicable in Upsert mode and will be ignored.");
            retireMissing = false;
        }

        // Detect → MapToCanonical via format adapter registry (§7.7 / P4)
        var adapted = _formatAdapters.Adapt(csvContent);
        preview.FileErrors.AddRange(adapted.FileErrors);
        preview.FileWarnings.AddRange(adapted.FileWarnings);
        if (!adapted.Success)
            return preview;

        // Check empty books status for Replace mode
        var chartStatus = await GetChartStatusAsync();
        preview.IsEmptyBooks = chartStatus.IsEmptyBooks;
        preview.CanReplace = chartStatus.CanReplace;

        if (isReplace && !chartStatus.IsEmptyBooks)
        {
            preview.FileErrors.Add(chartStatus.ReplaceBlockedReason ?? "Cannot replace chart of accounts because the company has posted transactions.");
        }

        // Fetch current accounts in DB
        var existingAccounts = await _context.Accounts.ToListAsync();
        var existingByCode = existingAccounts.ToDictionary(a => a.AccountCode, StringComparer.OrdinalIgnoreCase);

        var fileRows = new List<Dto.Financial.CoaImportRowPreviewDto>();
        var seenCodesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCodesToStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var canonical in adapted.Rows)
        {
            var errors = new List<string>(canonical.Errors);
            var warnings = new List<string>(canonical.Warnings);
            var code = canonical.AccountCode;
            var status = canonical.Status;

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (seenCodesInFile.Contains(code))
                {
                    errors.Add($"Duplicate AccountCode '{code}' in import file.");
                }
                else
                {
                    seenCodesInFile.Add(code);
                    fileCodesToStatus[code] = status.Equals("Archived", StringComparison.OrdinalIgnoreCase) ? "Archived" : "Active";
                }
            }

            fileRows.Add(new Dto.Financial.CoaImportRowPreviewDto
            {
                RowNumber = canonical.RowNumber,
                AccountCode = code,
                AccountName = canonical.AccountName,
                ParentAccountCode = canonical.ParentAccountCode,
                AccountClassId = canonical.AccountClassId,
                AccountClassName = canonical.AccountClassName,
                IsContraAccount = canonical.IsContraAccount,
                IsCash = canonical.IsCash,
                Description = canonical.Description,
                Status = status,
                Errors = errors,
                Warnings = warnings
            });
        }

        // Validate each row against DB and hierarchy
        var allCodesInPlay = new HashSet<string>(isReplace ? seenCodesInFile : existingByCode.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var c in seenCodesInFile) allCodesInPlay.Add(c);

        // Build parent map for cycle detection
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!isReplace)
        {
            foreach (var a in existingAccounts)
            {
                if (a.ParentAccountId.HasValue)
                {
                    var parent = existingAccounts.FirstOrDefault(p => p.Id == a.ParentAccountId.Value);
                    if (parent != null) parentMap[a.AccountCode] = parent.AccountCode;
                }
            }
        }
        foreach (var r in fileRows)
        {
            if (!string.IsNullOrWhiteSpace(r.AccountCode))
            {
                if (!string.IsNullOrWhiteSpace(r.ParentAccountCode))
                    parentMap[r.AccountCode] = r.ParentAccountCode;
                else
                    parentMap.Remove(r.AccountCode);
            }
        }

        foreach (var rowDto in fileRows)
        {
            if (string.IsNullOrWhiteSpace(rowDto.AccountCode))
            {
                rowDto.Action = "error";
                continue;
            }

            var codeExists = existingByCode.TryGetValue(rowDto.AccountCode, out var existingAccount);

            if (isAppend)
            {
                if (codeExists)
                {
                    rowDto.Action = "skip";
                    rowDto.Warnings.Add($"Account '{rowDto.AccountCode}' already exists in chart of accounts. Skipped in Append mode.");
                }
                else
                {
                    rowDto.Action = rowDto.Errors.Count > 0 ? "error" : "create";
                }
            }
            else if (isUpsert)
            {
                if (codeExists)
                {
                    rowDto.Action = rowDto.Errors.Count > 0 ? "error" : "update";

                    // Hard guard: class change on account with history
                    if (existingAccount!.AccountClassId != rowDto.AccountClassId)
                    {
                        var hasHistory = await _context.GeneralLedgerLines.AnyAsync(l => l.AccountId == existingAccount.Id)
                            || await _context.JournalEntryLines.AnyAsync(l => l.AccountId == existingAccount.Id);
                        if (hasHistory)
                        {
                            rowDto.Errors.Add($"Cannot change account class for account '{rowDto.AccountCode}' because it has transaction history.");
                            rowDto.Action = "error";
                        }
                    }

                    // Check protected rename
                    if (!string.Equals(existingAccount.AccountName, rowDto.AccountName, StringComparison.Ordinal))
                    {
                        var (isProt, protReason, _, _) = await GetAccountProtectionInfoAsync(existingAccount.Id);
                        if (isProt)
                        {
                            rowDto.Warnings.Add($"Account '{rowDto.AccountCode}' is a protected control account ({protReason}). Renaming may affect reporting labels.");
                        }
                    }

                    // Check archive of protected account
                    if (rowDto.Status == "Archived" && !existingAccount.ArchivedAt.HasValue)
                    {
                        var (isProt, protReason, _, _) = await GetAccountProtectionInfoAsync(existingAccount.Id);
                        if (isProt)
                        {
                            rowDto.Errors.Add($"Cannot archive protected account '{rowDto.AccountCode}' ({protReason}). Remap control account in Settings before archiving.");
                            rowDto.Action = "error";
                        }
                    }
                }
                else
                {
                    rowDto.Action = rowDto.Errors.Count > 0 ? "error" : "create";
                }
            }
            else // replace
            {
                rowDto.Action = rowDto.Errors.Count > 0 ? "error" : (codeExists ? "update" : "create");
            }

            // Parent rules
            if (!string.IsNullOrWhiteSpace(rowDto.ParentAccountCode))
            {
                if (string.Equals(rowDto.ParentAccountCode, rowDto.AccountCode, StringComparison.OrdinalIgnoreCase))
                {
                    rowDto.Errors.Add("Account cannot be its own parent.");
                    rowDto.Action = "error";
                }
                else if (!allCodesInPlay.Contains(rowDto.ParentAccountCode))
                {
                    rowDto.Errors.Add($"Parent account '{rowDto.ParentAccountCode}' does not exist in chart of accounts or import file.");
                    rowDto.Action = "error";
                }
                else
                {
                    // Check if parent is archived
                    bool parentWillBeArchived = false;
                    if (fileCodesToStatus.TryGetValue(rowDto.ParentAccountCode, out var fileParentStatus))
                    {
                        parentWillBeArchived = (fileParentStatus == "Archived");
                    }
                    else if (!isReplace && existingByCode.TryGetValue(rowDto.ParentAccountCode, out var dbParent))
                    {
                        parentWillBeArchived = dbParent.ArchivedAt.HasValue;
                    }

                    if (parentWillBeArchived && rowDto.Status != "Archived")
                    {
                        rowDto.Errors.Add($"Cannot assign parent '{rowDto.ParentAccountCode}' because it is archived. Unarchive the parent account first.");
                        rowDto.Action = "error";
                    }

                    // Cycle detection
                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rowDto.AccountCode };
                    var current = rowDto.ParentAccountCode;
                    while (!string.IsNullOrEmpty(current))
                    {
                        if (visited.Contains(current))
                        {
                            rowDto.Errors.Add($"Circular hierarchy detected involving account '{rowDto.AccountCode}' and parent '{rowDto.ParentAccountCode}'.");
                            rowDto.Action = "error";
                            break;
                        }
                        visited.Add(current);
                        parentMap.TryGetValue(current, out current);
                    }
                }
            }

            if (rowDto.Errors.Count > 0)
            {
                rowDto.Action = "error";
            }
        }

        // Control Remapping & Doomed Accounts check for Replace mode
        var requiredControlRoles = await GetRequiredControlRolesAsync();
        preview.ControlMappings = new List<Dto.Financial.ControlRoleMappingDto>();

        if (isReplace)
        {
            // Validate control mappings
            foreach (var role in requiredControlRoles)
            {
                string? targetCode = null;
                if (controlMappings != null && controlMappings.TryGetValue(role.RoleKey, out var userTarget) && !string.IsNullOrWhiteSpace(userTarget))
                {
                    targetCode = userTarget.Trim();
                }
                else if (!string.IsNullOrEmpty(role.CurrentAccountCode) && seenCodesInFile.Contains(role.CurrentAccountCode))
                {
                    // Auto-match if current code exists in new file
                    targetCode = role.CurrentAccountCode;
                }

                var roleDto = new Dto.Financial.ControlRoleMappingDto
                {
                    RoleKey = role.RoleKey,
                    RoleName = role.RoleName,
                    SourceType = role.SourceType,
                    SourceEntityId = role.SourceEntityId,
                    CurrentAccountId = role.CurrentAccountId,
                    CurrentAccountCode = role.CurrentAccountCode,
                    CurrentAccountName = role.CurrentAccountName,
                    TargetAccountCode = targetCode,
                    IsRequired = role.IsRequired
                };
                preview.ControlMappings.Add(roleDto);

                if (string.IsNullOrWhiteSpace(targetCode))
                {
                    preview.FileErrors.Add($"Control account for '{role.RoleName}' must be mapped to an account in the new chart.");
                }
                else if (!seenCodesInFile.Contains(targetCode))
                {
                    preview.FileErrors.Add($"Target account '{targetCode}' for control role '{role.RoleName}' does not exist in the import file.");
                }
            }

            // Validate doomed accounts (accounts in DB omitted from new file)
            var doomedAccounts = existingAccounts.Where(a => !seenCodesInFile.Contains(a.AccountCode)).ToList();
            preview.DeleteCount = doomedAccounts.Count;

            foreach (var doomed in doomedAccounts)
            {
                // ItemCategory links
                if (await _context.ItemCategories.AnyAsync(ic => ic.AdjustmentAccountId == doomed.Id ||
                                                                 ic.AssemblyAccountId == doomed.Id ||
                                                                 ic.CostOfGoodsSoldAccountId == doomed.Id ||
                                                                 ic.InventoryAccountId == doomed.Id ||
                                                                 ic.SalesAccountId == doomed.Id))
                {
                    preview.FileErrors.Add($"Account '{doomed.AccountCode}' ({doomed.AccountName}) cannot be replaced because it is linked to one or more Item Categories. Reassign item categories before replacing chart.");
                }

                // Item links
                if (await _context.Items.AnyAsync(i => i.InventoryAdjustmentAccountId == doomed.Id ||
                                                       i.CostOfGoodsSoldAccountId == doomed.Id ||
                                                       i.InventoryAccountId == doomed.Id ||
                                                       i.SalesAccountId == doomed.Id))
                {
                    preview.FileErrors.Add($"Account '{doomed.AccountCode}' ({doomed.AccountName}) cannot be replaced because it is linked to one or more Items. Reassign items before replacing chart.");
                }

                // Customer links
                if (await _context.Customers.AnyAsync(c => c.AccountsReceivableAccountId == doomed.Id ||
                                                           c.CustomerAdvancesAccountId == doomed.Id ||
                                                           c.PromptPaymentDiscountAccountId == doomed.Id ||
                                                           c.SalesAccountId == doomed.Id ||
                                                           c.SalesDiscountAccountId == doomed.Id))
                {
                    preview.FileErrors.Add($"Account '{doomed.AccountCode}' ({doomed.AccountName}) cannot be replaced because it is configured on one or more Customers. Reassign customer accounts before replacing chart.");
                }

                // Vendor links
                if (await _context.Vendors.AnyAsync(v => v.AccountsPayableAccountId == doomed.Id ||
                                                         v.PurchaseAccountId == doomed.Id ||
                                                         v.PurchaseDiscountAccountId == doomed.Id))
                {
                    preview.FileErrors.Add($"Account '{doomed.AccountCode}' ({doomed.AccountName}) cannot be replaced because it is configured on one or more Vendors. Reassign vendor accounts before replacing chart.");
                }

                // Contra links
                if (await _context.MainContraAccounts.AnyAsync(mca => mca.MainAccountId == doomed.Id || mca.RelatedContraAccountId == doomed.Id))
                {
                    preview.FileErrors.Add($"Account '{doomed.AccountCode}' ({doomed.AccountName}) cannot be replaced because it has active contra account mappings.");
                }
            }
        }
        else
        {
            preview.ControlMappings = requiredControlRoles;
        }

        // Validate Retire Missing in Upsert mode
        if (isUpsert && retireMissing)
        {
            var missingAccounts = existingAccounts
                .Where(a => !seenCodesInFile.Contains(a.AccountCode) && !a.ArchivedAt.HasValue)
                .ToList();

            foreach (var missing in missingAccounts)
            {
                var (isProt, protReason, _, _) = await GetAccountProtectionInfoAsync(missing.Id);
                if (isProt)
                {
                    preview.FileErrors.Add($"Cannot retire missing account '{missing.AccountCode}' ({missing.AccountName}) because it is a protected control account ({protReason}). Remap control account first or include it in the file.");
                }
                else
                {
                    preview.RetireCount++;
                }
            }
        }

        preview.Rows = fileRows;
        preview.TotalRows = fileRows.Count;
        preview.CreateCount = fileRows.Count(r => r.Action == "create");
        preview.UpdateCount = fileRows.Count(r => r.Action == "update");
        preview.SkipCount = fileRows.Count(r => r.Action == "skip");
        preview.ErrorCount = fileRows.Count(r => r.Action == "error");
        preview.WarningCount = fileRows.Sum(r => r.Warnings.Count) + preview.FileWarnings.Count;

        return preview;
    }

    public async Task<(Dto.Financial.CoaImportCommitResultDto? result, Dto.Financial.CoaImportPreviewDto? failedPreview)> CommitImportAsync(
        string csvContent,
        string mode,
        bool skipErrors = false,
        bool retireMissing = false,
        Dictionary<string, string>? controlMappings = null,
        bool isCallerOwner = false)
    {
        var preview = await ValidateImportAsync(csvContent, mode, retireMissing, controlMappings, isCallerOwner);
        if (preview.FileErrors.Count > 0)
        {
            return (null, preview);
        }

        if (preview.ErrorCount > 0 && !skipErrors)
        {
            return (null, preview);
        }

        bool isReplace = string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase);
        bool isUpsert = string.Equals(mode, "upsert", StringComparison.OrdinalIgnoreCase);

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Re-verify empty books & Owner inside transaction if Replace mode
            if (isReplace)
            {
                if (!isCallerOwner)
                {
                    preview.FileErrors.Add("Replace mode requires workspace Owner role.");
                    return (null, preview);
                }

                var chartStatus = await GetChartStatusAsync();
                if (!chartStatus.IsEmptyBooks)
                {
                    preview.FileErrors.Add(chartStatus.ReplaceBlockedReason ?? "Cannot replace chart of accounts because the company has posted transactions.");
                    return (null, preview);
                }
            }

            var rowsToProcess = skipErrors
                ? preview.Rows.Where(r => r.Action != "error").ToList()
                : preview.Rows.ToList();

            var existingAccounts = await _context.Accounts.ToListAsync();
            var existingByCode = existingAccounts.ToDictionary(a => a.AccountCode, StringComparer.OrdinalIgnoreCase);

            int created = 0;
            int updated = 0;
            int skipped = 0;
            int retired = 0;
            int deleted = 0;

            // 1. Process Updates
            var updates = rowsToProcess.Where(r => r.Action == "update").ToList();
            foreach (var u in updates)
            {
                if (existingByCode.TryGetValue(u.AccountCode, out var acct))
                {
                    acct.AccountName = u.AccountName;
                    acct.Description = u.Description;
                    acct.IsContraAccount = u.IsContraAccount;
                    acct.IsCash = u.IsCash;
                    acct.AccountClassId = u.AccountClassId;
                    if (u.Status == "Archived")
                        acct.ArchivedAt ??= DateTime.UtcNow;
                    else
                        acct.ArchivedAt = null;

                    _context.Accounts.Update(acct);
                    updated++;
                }
            }
            await _context.SaveChangesAsync();

            // 2. Process Creates (in topological order of parent dependency)
            var creates = rowsToProcess.Where(r => r.Action == "create").ToList();
            var remainingCreates = new List<Dto.Financial.CoaImportRowPreviewDto>(creates);

            var codeToIdMap = existingAccounts.ToDictionary(a => a.AccountCode, a => a.Id, StringComparer.OrdinalIgnoreCase);

            while (remainingCreates.Count > 0)
            {
                var readyBatch = remainingCreates.Where(r =>
                    string.IsNullOrEmpty(r.ParentAccountCode) ||
                    codeToIdMap.ContainsKey(r.ParentAccountCode)
                ).ToList();

                if (readyBatch.Count == 0)
                {
                    readyBatch = new List<Dto.Financial.CoaImportRowPreviewDto> { remainingCreates[0] };
                }

                foreach (var c in readyBatch)
                {
                    int? parentId = null;
                    if (!string.IsNullOrEmpty(c.ParentAccountCode) && codeToIdMap.TryGetValue(c.ParentAccountCode, out var pid))
                    {
                        parentId = pid;
                    }

                    var newAcct = new Core.Domain.Financials.Account
                    {
                        AccountCode = c.AccountCode,
                        AccountName = c.AccountName,
                        AccountClassId = c.AccountClassId,
                        ParentAccountId = parentId,
                        IsContraAccount = c.IsContraAccount,
                        IsCash = c.IsCash,
                        Description = c.Description,
                        ArchivedAt = (c.Status == "Archived") ? DateTime.UtcNow : null
                    };

                    _context.Accounts.Add(newAcct);
                    await _context.SaveChangesAsync();

                    codeToIdMap[c.AccountCode] = newAcct.Id;
                    existingByCode[c.AccountCode] = newAcct;
                    remainingCreates.Remove(c);
                    created++;
                }
            }

            // 3. Update ParentAccountId for any updates or creates whose parents resolved later
            var allWiredRows = rowsToProcess.Where(r => r.Action == "create" || r.Action == "update").ToList();
            foreach (var row in allWiredRows)
            {
                if (existingByCode.TryGetValue(row.AccountCode, out var acct))
                {
                    int? targetParentId = null;
                    if (!string.IsNullOrEmpty(row.ParentAccountCode) && codeToIdMap.TryGetValue(row.ParentAccountCode, out var pid))
                    {
                        targetParentId = pid;
                    }

                    if (acct.ParentAccountId != targetParentId)
                    {
                        acct.ParentAccountId = targetParentId;
                        _context.Accounts.Update(acct);
                    }
                }
            }
            await _context.SaveChangesAsync();

            // 4. If Replace mode, apply control role remappings and delete doomed accounts
            if (isReplace)
            {
                // Repoint GeneralLedgerSetting
                var gls = await _context.GeneralLedgerSettings.FirstOrDefaultAsync();
                if (gls != null)
                {
                    if (TryGetMappedId("PayableAccount", controlMappings, codeToIdMap, out var idPayable)) gls.PayableAccountId = idPayable;
                    if (TryGetMappedId("PurchaseDiscountAccount", controlMappings, codeToIdMap, out var idPDisc)) gls.PurchaseDiscountAccountId = idPDisc;
                    if (TryGetMappedId("GoodsReceiptNoteClearingAccount", controlMappings, codeToIdMap, out var idClr)) gls.GoodsReceiptNoteClearingAccountId = idClr;
                    if (TryGetMappedId("SalesDiscountAccount", controlMappings, codeToIdMap, out var idSDisc)) gls.SalesDiscountAccountId = idSDisc;
                    if (TryGetMappedId("ShippingChargeAccount", controlMappings, codeToIdMap, out var idShip)) gls.ShippingChargeAccountId = idShip;
                    if (TryGetMappedId("PermanentAccount", controlMappings, codeToIdMap, out var idPerm)) gls.PermanentAccountId = idPerm;
                    if (TryGetMappedId("IncomeSummaryAccount", controlMappings, codeToIdMap, out var idInc)) gls.IncomeSummaryAccountId = idInc;
                    _context.GeneralLedgerSettings.Update(gls);
                }

                // Repoint Taxes
                var taxes = await _context.Taxes.ToListAsync();
                foreach (var tax in taxes)
                {
                    if (TryGetMappedId($"Tax_Sales_{tax.Id}", controlMappings, codeToIdMap, out var idTaxSales)) tax.SalesAccountId = idTaxSales;
                    if (TryGetMappedId($"Tax_Purchase_{tax.Id}", controlMappings, codeToIdMap, out var idTaxPur)) tax.PurchasingAccountId = idTaxPur;
                    _context.Taxes.Update(tax);
                }

                // Repoint Banks through D2 eligibility (#413 1B).
                var banks = await _context.Banks.ToListAsync();
                foreach (var bank in banks)
                {
                    if (TryGetMappedId($"Bank_{bank.Id}", controlMappings, codeToIdMap, out var idBank))
                    {
                        _bankAccounts.EnsureEligiblePostingAccount(idBank, exceptBankId: bank.Id);
                        bank.AccountId = idBank;
                    }
                    _context.Banks.Update(bank);
                }

                await _context.SaveChangesAsync();

                // Leaf-first deletion of doomed accounts (accounts in DB omitted from new file)
                var fileCodes = rowsToProcess.Select(r => r.AccountCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var allCurrentDbAccounts = await _context.Accounts.ToListAsync();
                var doomedAccounts = allCurrentDbAccounts.Where(a => !fileCodes.Contains(a.AccountCode)).ToList();

                // Break parent links among doomed accounts to prevent cyclic foreign keys during deletion
                foreach (var d in doomedAccounts)
                {
                    d.ParentAccountId = null;
                    _context.Accounts.Update(d);
                }
                await _context.SaveChangesAsync();

                // Delete doomed accounts in batches
                _context.Accounts.RemoveRange(doomedAccounts);
                await _context.SaveChangesAsync();
                deleted = doomedAccounts.Count;
            }

            // 5. If Upsert mode and retireMissing enabled, archive omitted accounts
            if (isUpsert && retireMissing)
            {
                var fileCodes = rowsToProcess.Select(r => r.AccountCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var allCurrentDbAccounts = await _context.Accounts.ToListAsync();
                var toRetire = allCurrentDbAccounts.Where(a => !fileCodes.Contains(a.AccountCode) && !a.ArchivedAt.HasValue).ToList();

                foreach (var acct in toRetire)
                {
                    acct.ArchivedAt = DateTime.UtcNow;
                    _context.Accounts.Update(acct);
                    retired++;
                }
                await _context.SaveChangesAsync();
            }

            skipped = rowsToProcess.Count(r => r.Action == "skip");

            await tx.CommitAsync();

            string modeMsg = isReplace
                ? $"Chart replaced: {created} created, {updated} updated, {deleted} removed."
                : isUpsert && retireMissing
                    ? $"Import completed: {created} created, {updated} updated, {skipped} skipped, {retired} retired."
                    : $"Import completed: {created} created, {updated} updated, {skipped} skipped.";

            return (new Dto.Financial.CoaImportCommitResultDto
            {
                Mode = preview.Mode,
                TotalRows = rowsToProcess.Count,
                CreatedCount = created,
                UpdatedCount = updated,
                SkippedCount = skipped,
                RetiredCount = retired,
                DeletedCount = deleted,
                Message = modeMsg
            }, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            preview.FileErrors.Add($"Database transaction failed and rolled back: {ex.Message}");
            return (null, preview);
        }
    }

    private static bool TryGetMappedId(
        string roleKey,
        Dictionary<string, string>? controlMappings,
        Dictionary<string, int> codeToIdMap,
        out int mappedId)
    {
        mappedId = 0;
        if (controlMappings != null && controlMappings.TryGetValue(roleKey, out var code) && !string.IsNullOrWhiteSpace(code))
        {
            return codeToIdMap.TryGetValue(code.Trim(), out mappedId);
        }
        return false;
    }

}
