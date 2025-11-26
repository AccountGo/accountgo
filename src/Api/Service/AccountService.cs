using Api.Data;
using Core.Domain.Items;
using Dto.Financial;
using Microsoft.EntityFrameworkCore;

public class AccountService : IAccountService
{
    private readonly ApiDbContext _context;

    public AccountService(ApiDbContext context)
    {
        _context = context;
    }

    // Add a new account
    public async Task<Core.Domain.Financials.Account> AddAccountAsync(Core.Domain.Financials.Account newAccount)
    {
        // newAccount.Balance = 0; // Ensure read-only fields are initialized
        // newAccount.DebitBalance = 0;
        // newAccount.CreditBalance = 0;

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

        account.AccountName = updatedAccount.AccountName;

        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();

        return account;
    }


    // Delete an account
    public async Task<(Core.Domain.Financials.Account? account, string? errorMessage)> DeleteAccountAsync(string accountCode)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountCode == accountCode);
        if (account == null)
            return (null, "Account not found");

        // Check if account is referenced by ItemCategory
        var isReferencedByItemCategory = await _context.ItemCategories
            .AnyAsync(ic => ic.AdjustmentAccountId == account.Id || 
                           ic.AssemblyAccountId == account.Id ||
                           ic.CostOfGoodsSoldAccountId == account.Id ||
                           ic.InventoryAccountId == account.Id ||
                           ic.SalesAccountId == account.Id);
        
        if (isReferencedByItemCategory)
        {
            return (null, "Cannot delete account because it is being used by one or more item categories. Please remove the account reference from all item categories first.");
        }

        // Check if account is referenced by Items
        var isReferencedByItem = await _context.Items
            .AnyAsync(i => i.InventoryAdjustmentAccountId == account.Id ||
                          i.CostOfGoodsSoldAccountId == account.Id ||
                          i.InventoryAccountId == account.Id ||
                          i.SalesAccountId == account.Id);
        
        if (isReferencedByItem)
        {
            return (null, "Cannot delete account because it is being used by one or more items. Please remove the account reference from all items first.");
        }

        // Check if account has child accounts - THIS CHECK MUST REMAIN (user requirement)
        var hasChildAccounts = await _context.Accounts
            .AnyAsync(a => a.ParentAccountId == account.Id);
        
        if (hasChildAccounts)
        {
            return (null, "Cannot delete account because it has child accounts. Please delete or reassign all child accounts first.");
        }

        // Check for transaction history - these cannot be deleted
        var isReferencedByGeneralLedgerLine = await _context.GeneralLedgerLines
            .AnyAsync(gll => gll.AccountId == account.Id);
        
        if (isReferencedByGeneralLedgerLine)
        {
            return (null, "Cannot delete account because it has transaction history in the general ledger. Accounts with transaction history cannot be deleted.");
        }

        var isReferencedByJournalEntryLine = await _context.JournalEntryLines
            .AnyAsync(jel => jel.AccountId == account.Id);
        
        if (isReferencedByJournalEntryLine)
        {
            return (null, "Cannot delete account because it has transaction history in journal entries. Accounts with transaction history cannot be deleted.");
        }

        var isReferencedBySalesReceiptHeader = await _context.SalesReceiptHeaders
            .AnyAsync(srh => srh.AccountToDebitId == account.Id);
        
        if (isReferencedBySalesReceiptHeader)
        {
            return (null, "Cannot delete account because it is being used in sales receipts. This account has transaction history.");
        }

        var isReferencedBySalesReceiptLine = await _context.SalesReceiptLines
            .AnyAsync(srl => srl.AccountToCreditId == account.Id);
        
        if (isReferencedBySalesReceiptLine)
        {
            return (null, "Cannot delete account because it is being used in sales receipt lines. This account has transaction history.");
        }

        // For nullable references, we'll automatically clear them before deletion
        // This allows deletion of accounts that are only referenced in configuration settings

        try
        {
            // Clear Customer references (nullable fields)
            var customersWithReference = await _context.Customers
                .Where(c => c.AccountsReceivableAccountId == account.Id ||
                           c.CustomerAdvancesAccountId == account.Id ||
                           c.PromptPaymentDiscountAccountId == account.Id ||
                           c.SalesAccountId == account.Id ||
                           c.SalesDiscountAccountId == account.Id)
                .ToListAsync();
            
            foreach (var customer in customersWithReference)
            {
                if (customer.AccountsReceivableAccountId == account.Id) customer.AccountsReceivableAccountId = null;
                if (customer.CustomerAdvancesAccountId == account.Id) customer.CustomerAdvancesAccountId = null;
                if (customer.PromptPaymentDiscountAccountId == account.Id) customer.PromptPaymentDiscountAccountId = null;
                if (customer.SalesAccountId == account.Id) customer.SalesAccountId = null;
                if (customer.SalesDiscountAccountId == account.Id) customer.SalesDiscountAccountId = null;
            }

            // Clear Vendor references (nullable fields)
            var vendorsWithReference = await _context.Vendors
                .Where(v => v.AccountsPayableAccountId == account.Id ||
                           v.PurchaseAccountId == account.Id ||
                           v.PurchaseDiscountAccountId == account.Id)
                .ToListAsync();
            
            foreach (var vendor in vendorsWithReference)
            {
                if (vendor.AccountsPayableAccountId == account.Id) vendor.AccountsPayableAccountId = null;
                if (vendor.PurchaseAccountId == account.Id) vendor.PurchaseAccountId = null;
                if (vendor.PurchaseDiscountAccountId == account.Id) vendor.PurchaseDiscountAccountId = null;
            }

            // Clear Tax references (nullable fields)
            var taxesWithReference = await _context.Taxes
                .Where(t => t.PurchasingAccountId == account.Id ||
                           t.SalesAccountId == account.Id)
                .ToListAsync();
            
            foreach (var tax in taxesWithReference)
            {
                if (tax.PurchasingAccountId == account.Id) tax.PurchasingAccountId = null;
                if (tax.SalesAccountId == account.Id) tax.SalesAccountId = null;
            }

            // Clear Bank references (nullable fields)
            var banksWithReference = await _context.Banks
                .Where(b => b.AccountId == account.Id)
                .ToListAsync();
            
            foreach (var bank in banksWithReference)
            {
                bank.AccountId = null;
            }

            // Clear GeneralLedgerSetting references (nullable fields)
            var glSettingsWithReference = await _context.GeneralLedgerSettings
                .Where(gls => gls.GoodsReceiptNoteClearingAccountId == account.Id ||
                             gls.PayableAccountId == account.Id ||
                             gls.PurchaseDiscountAccountId == account.Id ||
                             gls.SalesDiscountAccountId == account.Id ||
                             gls.ShippingChargeAccountId == account.Id)
                .ToListAsync();
            
            foreach (var glSetting in glSettingsWithReference)
            {
                if (glSetting.GoodsReceiptNoteClearingAccountId == account.Id) glSetting.GoodsReceiptNoteClearingAccountId = null;
                if (glSetting.PayableAccountId == account.Id) glSetting.PayableAccountId = null;
                if (glSetting.PurchaseDiscountAccountId == account.Id) glSetting.PurchaseDiscountAccountId = null;
                if (glSetting.SalesDiscountAccountId == account.Id) glSetting.SalesDiscountAccountId = null;
                if (glSetting.ShippingChargeAccountId == account.Id) glSetting.ShippingChargeAccountId = null;
            }

            // Clear MainContraAccount references (these might not be nullable, so we'll delete the relationships)
            var contraAccountsWithReference = await _context.MainContraAccounts
                .Where(mca => mca.MainAccountId == account.Id ||
                             mca.RelatedContraAccountId == account.Id)
                .ToListAsync();
            
            _context.MainContraAccounts.RemoveRange(contraAccountsWithReference);

            // Clear ItemCategory references (nullable fields)
            var itemCategoriesWithReference = await _context.ItemCategories
                .Where(ic => ic.AdjustmentAccountId == account.Id ||
                           ic.AssemblyAccountId == account.Id ||
                           ic.CostOfGoodsSoldAccountId == account.Id ||
                           ic.InventoryAccountId == account.Id ||
                           ic.SalesAccountId == account.Id)
                .ToListAsync();
            
            foreach (var itemCategory in itemCategoriesWithReference)
            {
                if (itemCategory.AdjustmentAccountId == account.Id) itemCategory.AdjustmentAccountId = null;
                if (itemCategory.AssemblyAccountId == account.Id) itemCategory.AssemblyAccountId = null;
                if (itemCategory.CostOfGoodsSoldAccountId == account.Id) itemCategory.CostOfGoodsSoldAccountId = null;
                if (itemCategory.InventoryAccountId == account.Id) itemCategory.InventoryAccountId = null;
                if (itemCategory.SalesAccountId == account.Id) itemCategory.SalesAccountId = null;
            }

            // Clear Item references (nullable fields)
            var itemsWithReference = await _context.Items
                .Where(i => i.InventoryAdjustmentAccountId == account.Id ||
                           i.CostOfGoodsSoldAccountId == account.Id ||
                           i.InventoryAccountId == account.Id ||
                           i.SalesAccountId == account.Id)
                .ToListAsync();
            
            foreach (var item in itemsWithReference)
            {
                if (item.InventoryAdjustmentAccountId == account.Id) item.InventoryAdjustmentAccountId = null;
                if (item.CostOfGoodsSoldAccountId == account.Id) item.CostOfGoodsSoldAccountId = null;
                if (item.InventoryAccountId == account.Id) item.InventoryAccountId = null;
                if (item.SalesAccountId == account.Id) item.SalesAccountId = null;
            }

            // Save all the cleared references
            await _context.SaveChangesAsync();

            // Now delete the account
            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();
            return (account, null);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            // Handle foreign key constraint violations
            if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                // Try to extract the constraint name from the error message
                var constraintMatch = System.Text.RegularExpressions.Regex.Match(
                    sqlEx.Message, 
                    @"constraint ""([^""]+)""");
                
                string constraintName = constraintMatch.Success ? constraintMatch.Groups[1].Value : "unknown";
                
                // Map constraint names to user-friendly messages
                string errorMessage = constraintName switch
                {
                    var c when c.Contains("ItemCategory") => "Cannot delete account because it is being used by one or more item categories.",
                    var c when c.Contains("Item") && !c.Contains("ItemCategory") => "Cannot delete account because it is being used by one or more items.",
                    var c when c.Contains("Customer") => "Cannot delete account because it is being used by one or more customers.",
                    var c when c.Contains("Vendor") => "Cannot delete account because it is being used by one or more vendors.",
                    var c when c.Contains("Tax") => "Cannot delete account because it is being used in tax settings.",
                    var c when c.Contains("Bank") => "Cannot delete account because it is being used by one or more banks.",
                    var c when c.Contains("GeneralLedgerSetting") => "Cannot delete account because it is being used in general ledger settings.",
                    var c when c.Contains("MainContraAccount") => "Cannot delete account because it is being used in contra account relationships.",
                    var c when c.Contains("SalesReceipt") => "Cannot delete account because it is being used in sales receipts (transaction history).",
                    var c when c.Contains("GeneralLedgerLine") => "Cannot delete account because it has transaction history in the general ledger.",
                    var c when c.Contains("JournalEntryLine") => "Cannot delete account because it has transaction history in journal entries.",
                    var c when c.Contains("Account") && c.Contains("ParentAccountId") => "Cannot delete account because it has child accounts.",
                    _ => $"Cannot delete account because it is being used by other records in the system (constraint: {constraintName}). Please remove all references to this account first."
                };
                
                return (null, errorMessage);
            }
            throw; // Re-throw if it's a different error
        }
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


}