#nullable enable
using Core.Domain;
using Core.Domain.Financials;
using Dto.TaxSystem;
using TaxEntity = Core.Domain.TaxSystem.Tax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Administration
{
    public sealed class TaxSettingsOutcome
    {
        public int StatusCode { get; private init; }
        public string? Message { get; private init; }
        public TaxSetting? Tax { get; private init; }

        public static TaxSettingsOutcome Ok(TaxSetting tax) => new() { StatusCode = 200, Tax = tax };
        public static TaxSettingsOutcome Deleted() => new() { StatusCode = 204 };
        public static TaxSettingsOutcome Fail(int statusCode, string message) => new() { StatusCode = statusCode, Message = message };
    }

    public partial class AdministrationService
    {
        public const string SystemTaxMessage = "The system 'No tax' rate can't be changed.";
        public const string TaxInUseMessage = "This tax is used on invoices or bills. Archive it instead.";
        private const string SalesAccountCode = "20300";
        private const string PurchaseAccountCode = "10160";

        public IReadOnlyList<TaxSetting> ListTaxSettings()
        {
            var used = UsedTaxIds();
            return _taxRepo.GetAllIncluding(t => t.SalesAccount, t => t.PurchasingAccount)
                .OrderBy(t => t.TaxName)
                .AsEnumerable()
                .Select(t => ToSetting(t, used.Contains(t.Id)))
                .ToList();
        }

        public Task<TaxSettingsOutcome> CreateTaxSettingAsync(TaxSettingWrite write)
        {
            var error = Validate(write, null, out var name, out var code);
            if (error != null)
                return Task.FromResult(error);
            var accounts = ResolveAccounts(write, null, null);
            if (accounts.Error != null)
                return Task.FromResult(accounts.Error);

            var tax = new TaxEntity
            {
                TaxName = name,
                TaxCode = code,
                Rate = write.Rate,
                IsActive = true,
                SalesAccountId = accounts.SalesId,
                PurchasingAccountId = accounts.PurchaseId
            };
            _taxRepo.Insert(tax);
            return Task.FromResult(TaxSettingsOutcome.Ok(ToSetting(Reload(tax.Id), false)));
        }

        public Task<TaxSettingsOutcome> UpdateTaxSettingAsync(int id, TaxSettingWrite write)
        {
            var tax = _taxRepo.Table.FirstOrDefault(t => t.Id == id);
            if (tax == null)
                return Task.FromResult(TaxSettingsOutcome.Fail(404, "Tax not found."));
            if (IsSystem(tax))
                return Task.FromResult(TaxSettingsOutcome.Fail(400, SystemTaxMessage));

            var error = Validate(write, id, out var name, out var code);
            if (error != null)
                return Task.FromResult(error);
            // A missing account id keeps the account already stored. Create is the path that fills in the defaults.
            var accounts = ResolveAccounts(write, tax.SalesAccountId, tax.PurchasingAccountId);
            if (accounts.Error != null)
                return Task.FromResult(accounts.Error);

            tax.TaxName = name;
            tax.TaxCode = code;
            tax.Rate = write.Rate;
            tax.IsActive = write.IsActive;
            tax.SalesAccountId = accounts.SalesId;
            tax.PurchasingAccountId = accounts.PurchaseId;
            _taxRepo.Update(tax);
            return Task.FromResult(TaxSettingsOutcome.Ok(ToSetting(Reload(tax.Id), TaxIsUsed(tax.Id))));
        }

        public async Task<TaxSettingsOutcome> DeleteTaxSettingAsync(int id)
        {
            var tax = _taxRepo.Table.FirstOrDefault(t => t.Id == id);
            if (tax == null)
                return TaxSettingsOutcome.Fail(404, "Tax not found.");
            if (IsSystem(tax))
                return TaxSettingsOutcome.Fail(400, SystemTaxMessage);
            if (TaxIsUsed(tax.Id))
                return TaxSettingsOutcome.Fail(409, TaxInUseMessage);

            await _taxRepo.DeleteAsync(tax);
            return TaxSettingsOutcome.Deleted();
        }

        private TaxSettingsOutcome? Validate(TaxSettingWrite write, int? exceptId, out string name, out string code)
        {
            name = write?.TaxName?.Trim() ?? "";
            code = write?.TaxCode?.Trim().ToUpperInvariant() ?? "";
            if (write == null || name.Length == 0)
                return TaxSettingsOutcome.Fail(400, "Enter a tax name.");
            if (name.Length > 50)
                return TaxSettingsOutcome.Fail(400, "A tax name can be at most 50 characters.");
            if (code.Length == 0)
                return TaxSettingsOutcome.Fail(400, "Enter a tax code.");
            if (code.Length > 16)
                return TaxSettingsOutcome.Fail(400, "A tax code can be at most 16 characters.");
            if (write.Rate < 0 || write.Rate > 100)
                return TaxSettingsOutcome.Fail(400, "Rate must be between 0 and 100.");
            // The column is decimal(18, 2). A rate it cannot store is rejected rather than rounded.
            if (decimal.Round(write.Rate, 2) != write.Rate)
                return TaxSettingsOutcome.Fail(400, "Rate can have at most 2 decimal places.");

            var nameKey = name.ToLowerInvariant();
            var codeKey = code.ToLowerInvariant();
            var others = _taxRepo.Table.Where(t => exceptId == null || t.Id != exceptId.Value);
            if (others.Any(t => t.TaxName.ToLower() == nameKey))
                return TaxSettingsOutcome.Fail(400, $"A tax named {name} already exists.");
            if (others.Any(t => t.TaxCode.ToLower() == codeKey))
                return TaxSettingsOutcome.Fail(400, $"A tax with code {code} already exists.");
            return null;
        }

        /// <summary>
        /// On create, <paramref name="keepSalesId"/> and <paramref name="keepPurchaseId"/> are null, so a missing id
        /// means the default account. On edit they are the accounts already stored, and a missing id keeps them.
        /// </summary>
        private (TaxSettingsOutcome? Error, int SalesId, int PurchaseId) ResolveAccounts(TaxSettingWrite write, int? keepSalesId, int? keepPurchaseId)
        {
            var sales = AccountFor(write.SalesAccountId, keepSalesId, SalesAccountCode, AccountClasses.Liabilities, null);
            if (sales.Error != null)
                return (sales.Error, 0, 0);
            var purchase = AccountFor(write.PurchaseAccountId, keepPurchaseId, PurchaseAccountCode, AccountClasses.Assets, AccountClasses.Expense);
            if (purchase.Error != null)
                return (purchase.Error, 0, 0);
            return (null, sales.Id, purchase.Id);
        }

        private (TaxSettingsOutcome? Error, int Id) AccountFor(int? requestedId, int? keepId, string defaultCode, AccountClasses first, AccountClasses? second)
        {
            if (requestedId is not > 0 && keepId is > 0)
                return (null, keepId.Value);
            var error = ResolveAccount(requestedId, defaultCode, first, second, out var account);
            if (error != null)
                return (error, 0);
            return (null, account.Id);
        }

        private TaxSettingsOutcome? ResolveAccount(int? id, string defaultCode, AccountClasses first, AccountClasses? second, out Account account)
        {
            account = null!;
            var specified = id is > 0;
            var found = specified
                ? _accountRepo.Table.FirstOrDefault(a => a.Id == id)
                : _accountRepo.Table.FirstOrDefault(a => a.AccountCode == defaultCode);
            if (found == null)
                return TaxSettingsOutcome.Fail(400, specified
                    ? "That account is not in this workspace."
                    : $"Account {defaultCode} was not found in this workspace.");
            account = found;
            var accountId = account.Id;
            var allowed = account.AccountClassId == (int)first || (second.HasValue && account.AccountClassId == (int)second.Value);
            if (!allowed)
                return TaxSettingsOutcome.Fail(400, first == AccountClasses.Liabilities
                    ? "The sales tax account must be a liability."
                    : "The purchase tax account must be an asset or an expense.");
            if (_accountRepo.Table.Any(a => a.ParentAccountId == accountId))
                return TaxSettingsOutcome.Fail(400, "Choose a posting account. Summary accounts can't collect tax.");
            return null;
        }

        private HashSet<int> UsedTaxIds()
        {
            var ids = _salesInvoiceLineRepo.Table.Where(l => l.TaxId != null).Select(l => l.TaxId ?? 0).Where(taxId => taxId != 0).Distinct().ToList();
            ids.AddRange(_purchaseInvoiceLineRepo.Table.Where(l => l.TaxId != null).Select(l => l.TaxId ?? 0).Where(taxId => taxId != 0).Distinct());
            return ids.ToHashSet();
        }

        private bool TaxIsUsed(int id) =>
            _salesInvoiceLineRepo.Table.Any(l => l.TaxId == id) || _purchaseInvoiceLineRepo.Table.Any(l => l.TaxId == id);

        private TaxEntity Reload(int id) =>
            _taxRepo.GetAllIncluding(t => t.SalesAccount, t => t.PurchasingAccount).First(t => t.Id == id);

        private static bool IsSystem(TaxEntity tax) =>
            string.Equals(tax.TaxCode, "NONE", StringComparison.OrdinalIgnoreCase);

        private static TaxSetting ToSetting(TaxEntity tax, bool inUse) => new()
        {
            Id = tax.Id,
            TaxCode = tax.TaxCode,
            TaxName = tax.TaxName,
            Rate = tax.Rate,
            IsActive = tax.IsActive,
            IsSystem = IsSystem(tax),
            SalesAccountId = tax.SalesAccountId,
            SalesAccountCode = tax.SalesAccount?.AccountCode,
            SalesAccountName = tax.SalesAccount?.AccountName,
            PurchaseAccountId = tax.PurchasingAccountId,
            PurchaseAccountCode = tax.PurchasingAccount?.AccountCode,
            PurchaseAccountName = tax.PurchasingAccount?.AccountName,
            InUse = inUse
        };
    }
}
