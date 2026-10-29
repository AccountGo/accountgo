using Api.Data;
using Api.Filters;
using Core.Domain.Security;
using Dto.Banking;
using Dto.Financial;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services.Administration;
using Services.Banking;
using Services.Financial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    [RequireModule("financials", "reports")]
    //[Authorize]
    public class FinancialsController : BaseController
    {
        private static readonly Dictionary<int, string> CLASS_NAMES = new()
        {
            { 1, "Assets" },
            { 2, "Liabilities" },
            { 3, "Equity" },
            { 4, "Revenue" },
            { 5, "Expenses" },
            { 6, "Closing accounts" }
        };

        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;
        private readonly IAccountService _accountService;
        private readonly IBankAccountService _bankAccountService;
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;
        private readonly Core.Tenancy.ITenantContext _tenantContext;
        private readonly ApiDbContext _db;

        public FinancialsController(IAdministrationService adminService,
            IFinancialService financialService,
            IAccountService accountService,
            IBankAccountService bankAccountService,
            Core.Tenancy.ITenantGuard tenantGuard,
            Core.Tenancy.ITenantContext tenantContext,
            ApiDbContext db)
        {
            _adminService = adminService;
            _financialService = financialService;
            _accountService = accountService;
            _bankAccountService = bankAccountService;
            _tenantGuard = tenantGuard;
            _tenantContext = tenantContext;
            _db = db;
        }

        [HttpGet]
        [Route("CashBanks")] // api/Financials/CashBanks
        public IActionResult CashBanks()
        {
            // #413 1B: delegate to BankAccountService; keep the legacy DTO shape for Settings.
            var list = _bankAccountService.List(
                includeArchived: false,
                includeFullNumbers: false,
                includeCashHistoryCodes: true);
            var cashAndBanksDto = list.Accounts.Select(bank => new Bank
            {
                Id = bank.Id,
                Name = bank.Name,
                AccountNo = bank.AccountNumberMasked,
                BankName = bank.BankName,
                AccountId = bank.AccountId
            }).ToList();

            return new ObjectResult(cashAndBanksDto);
        }

        [HttpGet]
        [Route("Accounts")] // api/Financials/Accounts
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        public async Task<IActionResult> Accounts()
        {
            var accounts = _financialService.GetAccounts().ToList();
            var context = await LoadAccountValidationContextAsync();
            var accountTree = BuildAccountGrouping(accounts, null, context);

            return new ObjectResult(accountTree);
        }

        [HttpGet]
        [Route("Account")]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        public async Task<IActionResult> Account(int id)
        {
            var account = _financialService.GetAccount(id);
            if (account == null)
                return NotFound(new { message = $"Account #{id} not found." });

            var (isProtected, protectionReason, canDelete, deleteBlockedReason) =
                await _accountService.GetAccountProtectionInfoAsync(account.Id);

            var accountDto = new Dto.Financial.Account()
            {
                Id = account.Id,
                AccountClassId = account.AccountClassId,
                ParentAccountId = account.ParentAccountId,
                CompanyId = account.CompanyId,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                Description = account.Description,
                IsCash = account.IsCash,
                IsContraAccount = account.IsContraAccount,
                Archived = account.ArchivedAt.HasValue,
                ArchivedAt = account.ArchivedAt,
                IsProtected = isProtected,
                ProtectionReason = protectionReason,
                CanDelete = canDelete,
                DeleteBlockedReason = deleteBlockedReason,
                Balance = account.Balance,
                DebitBalance = account.DebitBalance,
                CreditBalance = account.CreditBalance
            };

            return new ObjectResult(accountDto);
        }


        [HttpGet]
        [Route("JournalEntries")] // api/Financials/JournalEntries
        [RequirePermission(SystemPermissions.FinancialsJournalView)]
        public IActionResult JournalEntries()
        {
            var journalEntries = _financialService.GetJournalEntries();
            var journalEntriesDto = new List<JournalEntry>();

            foreach (var je in journalEntries)
            {
                var journalEntryDto = new JournalEntry()
                {
                    Id = je.Id,
                    JournalDate = je.Date,
                    Memo = je.Memo,
                    ReferenceNo = je.ReferenceNo,
                    VoucherType = (int)je.VoucherType.GetValueOrDefault(),
                    Posted = je.Posted == true || je.GeneralLedgerHeaderId.HasValue
                };

                foreach (var line in je.JournalEntryLines)
                {
                    var lineDto = new JournalEntryLine()
                    {
                        Id = line.Id,
                        AccountId = line.AccountId,
                        Amount = line.Amount,
                        DrCr = (int)line.DrCr,
                        Memo = line.Memo
                    };

                    journalEntryDto.JournalEntryLines.Add(lineDto);
                }

                journalEntriesDto.Add(journalEntryDto);
            }

            return new ObjectResult(journalEntriesDto);
        }

        [HttpGet]
        [Route("JournalEntry")]
        [RequirePermission(SystemPermissions.FinancialsJournalView)]
        public IActionResult JournalEntry(int id)
        {
            var je = _financialService.GetJournalEntry(id);
            if (je == null)
                return NotFound(new { message = $"Journal entry #{id} not found." });

            var journalEntryDto = new JournalEntry()
            {
                Id = je.Id,
                JournalDate = je.Date,
                Memo = je.Memo,
                ReferenceNo = je.ReferenceNo,
                VoucherType = (int)je.VoucherType.GetValueOrDefault(),
                Posted = je.Posted == true || je.GeneralLedgerHeaderId.HasValue
            };

            foreach (var line in je.JournalEntryLines)
            {
                var lineDto = new JournalEntryLine()
                {
                    Id = line.Id,
                    AccountId = line.AccountId,
                    Amount = line.Amount,
                    DrCr = (int)line.DrCr,
                    Memo = line.Memo
                };

                journalEntryDto.JournalEntryLines.Add(lineDto);
            }

            // is this journal entry ready for posting?
            if (!journalEntryDto.Posted.GetValueOrDefault()
                && journalEntryDto.JournalEntryLines.Count >= 2
                && (journalEntryDto.debitAmount == journalEntryDto.creditAmount))
            {
                journalEntryDto.ReadyForPosting = true;
            }

            return new ObjectResult(journalEntryDto);
        }

        [HttpPost]
        [Route("PostJournalEntry")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsJournalPost)]
        public IActionResult PostJournalEntry([FromBody] JournalEntry journalEntryDto)
        {
            string[]? errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    // Collect per-error messages; do not index Errors by ModelState.ErrorCount (ArgumentOutOfRange).
                    errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToArray();

                    return new BadRequestObjectResult(errors);
                }

                var je = _financialService.GetJournalEntry(journalEntryDto.Id, false);
                if (je == null)
                    return NotFound(new { message = "Journal entry not found." });

                _financialService.UpdateJournalEntry(je, true);

                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException or Services.Banking.ReconciledPeriodConfirmationRequiredException) throw;
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("SaveJournalEntry")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsJournalCreate)]
        public IActionResult SaveJournalEntry([FromBody] JournalEntry journalEntryDto)
        {
            string[]? errors = null;

            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    int idx = 0;

                    foreach (var val in ModelState.Values)
                    {
                        foreach (var err in val.Errors)
                        {
                            if (idx < errors.Length)
                            {
                                errors[idx++] = err.ErrorMessage;
                            }
                        }
                    }

                    return new BadRequestObjectResult(errors);
                }

                // Split lines to the same account (different memos) are allowed; Dr/Cr balance is enforced on post.
                if (journalEntryDto.JournalEntryLines.Count < 2
                    || journalEntryDto.JournalEntryLines.Any(l => !l.AccountId.HasValue || l.Amount.GetValueOrDefault() <= 0
                        || (l.DrCr != 1 && l.DrCr != 2))
                    || !Enum.IsDefined(typeof(Core.Domain.JournalVoucherTypes), journalEntryDto.VoucherType.GetValueOrDefault()))
                    return BadRequest(new { message = "Provide a voucher type and at least two positive debit or credit lines with valid accounts." });

                bool isNew = journalEntryDto.Id == 0;
                Core.Domain.Financials.JournalEntryHeader? journalEntry = null;

                if (isNew)
                {
                    journalEntry = new Core.Domain.Financials.JournalEntryHeader();
                }
                else
                {
                    journalEntry = _financialService.GetJournalEntry(journalEntryDto.Id);
                    if (journalEntry == null)
                        return NotFound(new { message = "Journal entry not found." });
                    if (journalEntry.Posted == true || journalEntry.GeneralLedgerHeaderId.HasValue)
                        return BadRequest(new { message = "Posted journal entries cannot be edited." });

                    var dtoLineIds = journalEntryDto.JournalEntryLines
                        .Where(l => l.Id != 0)
                        .Select(l => l.Id)
                        .ToHashSet();

                    var linesToRemove = journalEntry.JournalEntryLines
                        .Where(l => l.Id != 0 && !dtoLineIds.Contains(l.Id))
                        .ToList();

                    foreach (var lineToRemove in linesToRemove)
                    {
                        journalEntry.JournalEntryLines.Remove(lineToRemove);
                    }
                }

                journalEntry.Date = journalEntryDto.JournalDate;
                journalEntry.VoucherType = (Core.Domain.JournalVoucherTypes)journalEntryDto.VoucherType.GetValueOrDefault();
                journalEntry.ReferenceNo = journalEntryDto.ReferenceNo;
                journalEntry.Memo = journalEntryDto.Memo;

                foreach (var line in journalEntryDto.JournalEntryLines)
                {
                    _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(line.AccountId, nameof(line.AccountId));

                    if (!isNew)
                    {
                        var existingLine = journalEntry.JournalEntryLines
                            .Where(j => j.Id == line.Id && line.Id != 0)
                            .FirstOrDefault();

                        if (existingLine != null)
                        {
                            existingLine.AccountId = line.AccountId.GetValueOrDefault();
                            existingLine.DrCr = (Core.Domain.DrOrCrSide)line.DrCr;
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Memo = line.Memo;
                        }
                        else
                        {
                            var journalLine = new Core.Domain.Financials.JournalEntryLine();
                            journalLine.AccountId = line.AccountId.GetValueOrDefault();
                            journalLine.DrCr = (Core.Domain.DrOrCrSide)line.DrCr;
                            journalLine.Amount = line.Amount.GetValueOrDefault();
                            journalLine.Memo = line.Memo;
                            journalEntry.JournalEntryLines.Add(journalLine);
                        }
                    }
                    else
                    {
                        var journalLine = new Core.Domain.Financials.JournalEntryLine();
                        journalLine.AccountId = line.AccountId.GetValueOrDefault();
                        journalLine.DrCr = (Core.Domain.DrOrCrSide)line.DrCr;
                        journalLine.Amount = line.Amount.GetValueOrDefault();
                        journalLine.Memo = line.Memo;
                        journalEntry.JournalEntryLines.Add(journalLine);
                    }
                }

                if (isNew)
                {
                    _financialService.AddJournalEntry(journalEntry);
                }
                else
                {
                    _financialService.UpdateJournalEntry(journalEntry, false);
                }

                return Ok(new { id = journalEntry.Id });
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException or Services.Banking.ReconciledPeriodConfirmationRequiredException) throw;
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpGet]
        [Route("GeneralLedger")]
        [RequirePermission(SystemPermissions.FinancialsJournalView)]
        public ActionResult GeneralLedger(DateTime? from = default(DateTime?),
            DateTime? to = default(DateTime?),
            string? accountCode = null,
            int? transactionNo = null,
            Core.Domain.DocumentTypes? documentType = null,
            int skip = 0,
            int take = Services.Financial.FinancialService.GeneralLedgerDefaultPageSize)
        {
            if (!ModelState.IsValid || (from.HasValue && to.HasValue && from.Value > to.Value))
                return BadRequest(new { message = "Provide a valid date range and filters: from must not be after to." });
            if (documentType.HasValue && !Enum.IsDefined(typeof(Core.Domain.DocumentTypes), documentType.Value))
                return BadRequest(new { message = "Provide a valid document type." });
            if (skip < 0 || take < 1 || take > Services.Financial.FinancialService.GeneralLedgerMaxPageSize)
                return BadRequest(new { message = $"Provide skip >= 0 and take between 1 and {Services.Financial.FinancialService.GeneralLedgerMaxPageSize}." });
            try
            {
                return Ok(_financialService.GetGeneralLedgerTransactions(from, to, accountCode, transactionNo, documentType, skip, take));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        [Route("trialbalance")]
        [RequirePermission(SystemPermissions.ReportsView)]
        public ActionResult TrialBalance(DateTime? from = null, DateTime? to = null, Core.Domain.AccountingBasis basis = Core.Domain.AccountingBasis.Accrual)
        {
            if (!Enum.IsDefined(typeof(Core.Domain.AccountingBasis), basis)
                || (ModelState.TryGetValue(nameof(basis), out var basisState) && basisState.Errors.Count > 0))
                return BadRequest(new { message = "Provide a valid accounting basis: Accrual or Cash." });
            if (!ModelState.IsValid || (from.HasValue && to.HasValue && from.Value > to.Value))
                return BadRequest(new { message = "Provide a valid date range: from must not be after to." });
            var Dto = _financialService.TrialBalance(from, to, basis);
            return new ObjectResult(Dto);
        }

        [HttpGet]
        [Route("BalanceSheet")]
        [RequirePermission(SystemPermissions.ReportsView)]
        public ActionResult BalanceSheet(DateTime? from = null, DateTime? to = null, Core.Domain.AccountingBasis basis = Core.Domain.AccountingBasis.Accrual)
        {
            if (!Enum.IsDefined(typeof(Core.Domain.AccountingBasis), basis)
                || (ModelState.TryGetValue(nameof(basis), out var basisState) && basisState.Errors.Count > 0))
                return BadRequest(new { message = "Provide a valid accounting basis: Accrual or Cash." });
            if (!ModelState.IsValid || (from.HasValue && to.HasValue && from.Value > to.Value))
                return BadRequest(new { message = "Provide a valid date range: from must not be after to." });
            var dto = _financialService.BalanceSheet(from, to, basis).ToList();
            return new ObjectResult(dto);
        }

        [HttpGet]
        [Route("IncomeStatement")]
        [RequirePermission(SystemPermissions.ReportsView)]
        public IActionResult IncomeStatement(DateTime? from = null, DateTime? to = null, Core.Domain.AccountingBasis basis = Core.Domain.AccountingBasis.Accrual)
        {
            if (!Enum.IsDefined(typeof(Core.Domain.AccountingBasis), basis)
                || (ModelState.TryGetValue(nameof(basis), out var basisState) && basisState.Errors.Count > 0))
                return BadRequest(new { message = "Provide a valid accounting basis: Accrual or Cash." });
            if (!ModelState.IsValid || (from.HasValue && to.HasValue && from.Value > to.Value))
                return BadRequest(new { message = "Provide a valid date range: from must not be after to." });
            return Ok(_financialService.IncomeStatement(from, to, basis));
        }

        [HttpGet]
        [Route("CashFlow")]
        [RequirePermission(SystemPermissions.ReportsView)]
        public IActionResult CashFlow(DateTime? from = null, DateTime? to = null, Core.Domain.AccountingBasis basis = Core.Domain.AccountingBasis.Accrual)
        {
            if (!Enum.IsDefined(typeof(Core.Domain.AccountingBasis), basis)
                || (ModelState.TryGetValue(nameof(basis), out var basisState) && basisState.Errors.Count > 0))
                return BadRequest(new { message = "Provide a valid accounting basis: Accrual or Cash." });
            if (!ModelState.IsValid || (from.HasValue && to.HasValue && from.Value > to.Value))
                return BadRequest(new { message = "Provide a valid date range: from must not be after to." });
            return Ok(_financialService.CashFlow(from, to, basis));
        }

        #region CRUD for Accounts

        [HttpGet]
        [Route("GetAccount/{accountCode}")]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        public async Task<IActionResult> GetAccountByCode(string accountCode)
        {
            var account = await _accountService.GetAccountByCodeAsync(accountCode);
            if (account == null)
                return NotFound();

            var (isProtected, protectionReason, canDelete, deleteBlockedReason) =
                await _accountService.GetAccountProtectionInfoAsync(account.Id);

            return Ok(new Account
            {
                Id = account.Id,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                AccountClassId = account.AccountClassId,
                ParentAccountId = account.ParentAccountId,
                Description = account.Description,
                IsCash = account.IsCash,
                IsContraAccount = account.IsContraAccount,
                Archived = account.ArchivedAt.HasValue,
                ArchivedAt = account.ArchivedAt,
                IsProtected = isProtected,
                ProtectionReason = protectionReason,
                CanDelete = canDelete,
                DeleteBlockedReason = deleteBlockedReason,
                Balance = 0,
                DebitBalance = 0,
                CreditBalance = 0
            });
        }

        [HttpPost]
        [Route("AddAccount")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public async Task<IActionResult> AddAccount([FromBody] Account newAccountDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (newAccountDto.ParentAccountId.HasValue)
            {
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(newAccountDto.ParentAccountId, nameof(newAccountDto.ParentAccountId));
            }

            var newAccount = new Core.Domain.Financials.Account
            {
                AccountCode = newAccountDto.AccountCode,
                AccountName = newAccountDto.AccountName,
                AccountClassId = newAccountDto.AccountClassId,
                ParentAccountId = newAccountDto.ParentAccountId,
                Description = newAccountDto.Description,
                IsCash = newAccountDto.IsCash,
                IsContraAccount = newAccountDto.IsContraAccount
            };

            Core.Domain.Financials.Account createdAccount;
            try
            {
                createdAccount = await _accountService.AddAccountAsync(newAccount);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }

            var accountDto = new Account
            {
                Id = createdAccount.Id,
                AccountCode = createdAccount.AccountCode,
                AccountName = createdAccount.AccountName,
                AccountClassId = createdAccount.AccountClassId,
                ParentAccountId = createdAccount.ParentAccountId,
                Description = createdAccount.Description,
                IsCash = createdAccount.IsCash,
                IsContraAccount = createdAccount.IsContraAccount,
                Archived = createdAccount.ArchivedAt.HasValue,
                ArchivedAt = createdAccount.ArchivedAt,
                Balance = 0,
                DebitBalance = 0,
                CreditBalance = 0
            };

            return CreatedAtAction(nameof(GetAccountByCode), new { accountCode = createdAccount.AccountCode }, accountDto);
        }

        [HttpPut]
        [Route("UpdateAccount/{accountCode}")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public async Task<IActionResult> UpdateAccount(string accountCode, [FromBody] Account updatedAccountDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (updatedAccountDto.ParentAccountId.HasValue)
            {
                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(updatedAccountDto.ParentAccountId, nameof(updatedAccountDto.ParentAccountId));
            }

            Core.Domain.Financials.Account? updatedAccount;
            try
            {
                updatedAccount = await _accountService.UpdateAccountAsync(accountCode, new Core.Domain.Financials.Account
                {
                    AccountCode = updatedAccountDto.AccountCode,
                    AccountName = updatedAccountDto.AccountName,
                    AccountClassId = updatedAccountDto.AccountClassId,
                    ParentAccountId = updatedAccountDto.ParentAccountId,
                    Description = updatedAccountDto.Description,
                    IsCash = updatedAccountDto.IsCash,
                    IsContraAccount = updatedAccountDto.IsContraAccount
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            if (updatedAccount == null)
                return NotFound(new { message = $"Account '{accountCode}' not found." });

            return Ok(new Account
            {
                Id = updatedAccount.Id,
                AccountCode = updatedAccount.AccountCode,
                AccountName = updatedAccount.AccountName,
                AccountClassId = updatedAccount.AccountClassId,
                ParentAccountId = updatedAccount.ParentAccountId,
                Description = updatedAccount.Description,
                IsCash = updatedAccount.IsCash,
                IsContraAccount = updatedAccount.IsContraAccount,
                Archived = updatedAccount.ArchivedAt.HasValue,
                ArchivedAt = updatedAccount.ArchivedAt,
                Balance = 0,
                DebitBalance = 0,
                CreditBalance = 0
            });
        }

        [HttpDelete]
        [Route("DeleteAccount/{accountCode}")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public async Task<IActionResult> DeleteAccount(string accountCode)
        {
            var (account, errorMessage) = await _accountService.DeleteAccountAsync(accountCode);

            if (account == null)
            {
                if (errorMessage == "Account not found")
                    return NotFound(new { message = errorMessage });
                else
                    return BadRequest(new { message = errorMessage });
            }

            return NoContent();
        }

        [HttpPost]
        [Route("ArchiveAccount/{accountCode}")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public async Task<IActionResult> ArchiveAccount(string accountCode)
        {
            var (account, errorMessage) = await _accountService.ArchiveAccountAsync(accountCode);
            if (account == null)
            {
                if (errorMessage == "Account not found")
                    return NotFound(new { message = errorMessage });
                return BadRequest(new { message = errorMessage });
            }

            return Ok(new Account
            {
                Id = account.Id,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                Archived = true,
                ArchivedAt = account.ArchivedAt
            });
        }

        [HttpPost]
        [Route("UnarchiveAccount/{accountCode}")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public async Task<IActionResult> UnarchiveAccount(string accountCode)
        {
            var (account, errorMessage) = await _accountService.UnarchiveAccountAsync(accountCode);
            if (account == null)
            {
                if (errorMessage == "Account not found")
                    return NotFound(new { message = errorMessage });
                return BadRequest(new { message = errorMessage });
            }

            return Ok(new Account
            {
                Id = account.Id,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                Archived = false,
                ArchivedAt = null
            });
        }

        #endregion

        #region Export Endpoints

        [HttpGet]
        [Route("ExportStructure")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public IActionResult ExportStructure()
        {
            var accounts = _financialService.GetAccounts().ToList();
            var csv = GenerateStructureCsv(accounts);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "chart-of-accounts-structure.csv");
        }

        [HttpGet]
        [Route("ExportSnapshot")]
        [RequireModule("financials", "reports")]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        public IActionResult ExportSnapshot()
        {
            var accounts = _financialService.GetAccounts().ToList();
            var csv = GenerateSnapshotCsv(accounts);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "chart-of-accounts-snapshot.csv");
        }

        [HttpGet]
        [Route("ExportTemplate")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        public IActionResult ExportTemplate()
        {
            var csv = GenerateSampleStructureTemplate();
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "accountgo-coa-structure-template.csv");
        }

        [HttpGet]
        [Route("ExportAccounts")]
        [RequireModule("financials")]
        public IActionResult ExportAccounts([FromQuery] string mode = "structure")
        {
            if (string.Equals(mode, "snapshot", StringComparison.OrdinalIgnoreCase))
                return ExportSnapshot();
            if (string.Equals(mode, "template", StringComparison.OrdinalIgnoreCase))
                return ExportTemplate();
            return ExportStructure();
        }

        [HttpGet]
        [Route("ChartStatus")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        public async Task<IActionResult> GetChartStatus()
        {
            var status = await _accountService.GetChartStatusAsync();
            return Ok(status);
        }

        [HttpPost]
        [Route("ImportAccounts/Validate")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public async Task<IActionResult> ValidateImport([FromQuery] string? mode = null)
        {
            try
            {
                var (csv, importMode, _, retireMissing, controlMappings) = await ExtractImportPayloadAsync(mode);
                if (string.IsNullOrWhiteSpace(csv))
                    return BadRequest(new { message = "No CSV file or content provided." });

                bool isCallerOwner = _tenantContext.Role == Core.Domain.CompanyMemberRole.Owner;
                var preview = await _accountService.ValidateImportAsync(csv, importMode, retireMissing, controlMappings, isCallerOwner);
                return Ok(preview);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.ToString() });
            }
        }

        [HttpPost]
        [Route("ImportAccounts/Commit")]
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public async Task<IActionResult> CommitImport([FromQuery] string? mode = null, [FromQuery] bool skipErrors = false)
        {
            var (csv, importMode, skip, retireMissing, controlMappings) = await ExtractImportPayloadAsync(mode, skipErrors);
            if (string.IsNullOrWhiteSpace(csv))
                return BadRequest(new { message = "No CSV file or content provided." });

            bool isCallerOwner = _tenantContext.Role == Core.Domain.CompanyMemberRole.Owner;

            // Strict Owner gating for Replace and Retire-missing (§9, D9, Condition 4)
            if ((importMode == "replace" || retireMissing) && !isCallerOwner)
            {
                return StatusCode(403, new { message = "Replace mode and Retire Missing require workspace Owner role." });
            }

            var (result, failedPreview) = await _accountService.CommitImportAsync(csv, importMode, skip, retireMissing, controlMappings, isCallerOwner);
            if (result != null)
                return Ok(result);

            return BadRequest(failedPreview);
        }

        private async Task<(string? csv, string mode, bool skipErrors, bool retireMissing, Dictionary<string, string>? controlMappings)> ExtractImportPayloadAsync(string? queryMode = null, bool querySkipErrors = false)
        {
            string? csv = null;
            string mode = queryMode ?? "append";
            bool skipErrors = querySkipErrors;
            bool retireMissing = false;
            Dictionary<string, string>? controlMappings = null;

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                if (form.Files.Count > 0)
                {
                    using var reader = new System.IO.StreamReader(form.Files[0].OpenReadStream(), Encoding.UTF8);
                    csv = await reader.ReadToEndAsync();
                }
                else if (form.TryGetValue("csvContent", out var formCsv))
                {
                    csv = formCsv.ToString();
                }
                if (form.TryGetValue("mode", out var formMode) && !string.IsNullOrWhiteSpace(formMode))
                {
                    mode = formMode.ToString();
                }
                if (form.TryGetValue("skipErrors", out var formSkip) && bool.TryParse(formSkip, out var b))
                {
                    skipErrors = b;
                }
                if (form.TryGetValue("retireMissing", out var formRetire) && bool.TryParse(formRetire, out var rm))
                {
                    retireMissing = rm;
                }
                if (form.TryGetValue("controlMappings", out var formCm) && !string.IsNullOrWhiteSpace(formCm))
                {
                    try
                    {
                        controlMappings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(formCm.ToString());
                    }
                    catch { }
                }
            }
            else if (Request.ContentType != null && Request.ContentType.Contains("application/json"))
            {
                using var reader = new System.IO.StreamReader(Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("csvContent", out var cProp)) csv = cProp.GetString();
                        if (doc.RootElement.TryGetProperty("mode", out var mProp)) mode = mProp.GetString() ?? mode;
                        if (doc.RootElement.TryGetProperty("skipErrors", out var sProp)) skipErrors = sProp.GetBoolean();
                        if (doc.RootElement.TryGetProperty("retireMissing", out var rmProp)) retireMissing = rmProp.GetBoolean();
                        if (doc.RootElement.TryGetProperty("controlMappings", out var cmProp) && cmProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            controlMappings = new Dictionary<string, string>();
                            foreach (var prop in cmProp.EnumerateObject())
                            {
                                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                                    controlMappings[prop.Name] = prop.Value.GetString() ?? "";
                            }
                        }
                    }
                    catch { }
                }
            }
            else
            {
                // Fallback: raw body as CSV text
                using var reader = new System.IO.StreamReader(Request.Body, Encoding.UTF8);
                csv = await reader.ReadToEndAsync();
            }

            return (csv, mode, skipErrors, retireMissing, controlMappings);
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        private static string GenerateStructureCsv(IList<Core.Domain.Financials.Account> allAccounts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AccountGo-COA-v1");
            sb.AppendLine("AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status");

            var sortedAccounts = allAccounts
                .OrderBy(a => a.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var a in sortedAccounts)
            {
                var parentCode = a.ParentAccountId.HasValue
                    ? allAccounts.FirstOrDefault(p => p.Id == a.ParentAccountId.Value)?.AccountCode ?? ""
                    : "";
                var className = a.AccountClass?.Name ?? (CLASS_NAMES.TryGetValue(a.AccountClassId, out var cn) ? cn : "Assets");
                var contra = a.IsContraAccount ? "TRUE" : "FALSE";
                var cash = a.IsCash ? "TRUE" : "FALSE";
                var status = a.ArchivedAt.HasValue ? "Archived" : "Active";
                sb.AppendLine($"{EscapeCsv(a.AccountCode)},{EscapeCsv(a.AccountName)},{EscapeCsv(parentCode)},{EscapeCsv(className)},{contra},{cash},{EscapeCsv(a.Description)},{status}");
            }

            return sb.ToString();
        }

        private static string GenerateSnapshotCsv(IList<Core.Domain.Financials.Account> allAccounts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AccountGo-COA-Snapshot-v1");
            sb.AppendLine("AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status,Level,NormalBalance,Header,Balance");

            var flatRows = FlattenHierarchyForExport(allAccounts, null, 1);

            foreach (var (a, level, rollup, hasChildren) in flatRows)
            {
                var parentCode = a.ParentAccountId.HasValue
                    ? allAccounts.FirstOrDefault(p => p.Id == a.ParentAccountId.Value)?.AccountCode ?? ""
                    : "";
                var className = a.AccountClass?.Name ?? (CLASS_NAMES.TryGetValue(a.AccountClassId, out var cn) ? cn : "Assets");
                var contra = a.IsContraAccount ? "TRUE" : "FALSE";
                var cash = a.IsCash ? "TRUE" : "FALSE";
                var status = a.ArchivedAt.HasValue ? "Archived" : "Active";
                var normalBalance = a.DrOrCrSide.ToString();
                var header = hasChildren ? "yes" : "no";
                var balance = rollup.ToString("F2", CultureInfo.InvariantCulture);

                sb.AppendLine($"{EscapeCsv(a.AccountCode)},{EscapeCsv(a.AccountName)},{EscapeCsv(parentCode)},{EscapeCsv(className)},{contra},{cash},{EscapeCsv(a.Description)},{status},{level},{EscapeCsv(normalBalance)},{header},{balance}");
            }

            return sb.ToString();
        }

        private static List<(Core.Domain.Financials.Account account, int level, decimal rollup, bool hasChildren)> FlattenHierarchyForExport(
            IList<Core.Domain.Financials.Account> allAccounts, int? parentAccountId, int level)
        {
            var result = new List<(Core.Domain.Financials.Account, int, decimal, bool)>();
            var children = allAccounts.Where(a => a.ParentAccountId == parentAccountId)
                .OrderBy(a => a.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var child in children)
            {
                var hasSubAccounts = allAccounts.Any(a => a.ParentAccountId == child.Id);
                var rollup = CalculateAccountRollup(child, allAccounts);
                result.Add((child, level, rollup, hasSubAccounts));
                result.AddRange(FlattenHierarchyForExport(allAccounts, child.Id, level + 1));
            }

            return result;
        }

        private static decimal CalculateAccountRollup(Core.Domain.Financials.Account account, IList<Core.Domain.Financials.Account> allAccounts)
        {
            decimal sum = account.Balance;
            var directChildren = allAccounts.Where(a => a.ParentAccountId == account.Id).ToList();
            foreach (var child in directChildren)
            {
                sum += CalculateAccountRollup(child, allAccounts);
            }
            return sum;
        }

        private static string GenerateSampleStructureTemplate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AccountGo-COA-v1");
            sb.AppendLine("AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status");
            sb.AppendLine("10000,Assets,,Assets,FALSE,FALSE,Asset summary account,Active");
            sb.AppendLine("10100,Current Assets,10000,Assets,FALSE,FALSE,Current assets group,Active");
            sb.AppendLine("10110,Cash & Bank,10100,Assets,FALSE,TRUE,Cash & bank holdings,Active");
            sb.AppendLine("10111,Operating Bank Account,10110,Assets,FALSE,TRUE,Main checking account,Active");
            sb.AppendLine("10120,Accounts Receivable,10100,Assets,FALSE,FALSE,Trade debtors,Active");
            sb.AppendLine("10121,Allowance for Doubtful Accounts,10100,Assets,TRUE,FALSE,Contra asset,Active");
            sb.AppendLine("20000,Liabilities,,Liabilities,FALSE,FALSE,Liabilities summary,Active");
            sb.AppendLine("20100,Current Liabilities,20000,Liabilities,FALSE,FALSE,Current liabilities,Active");
            sb.AppendLine("20110,Accounts Payable,20100,Liabilities,FALSE,FALSE,Trade creditors,Active");
            sb.AppendLine("30000,Equity,,Equity,FALSE,FALSE,Equity summary,Active");
            sb.AppendLine("30100,Owner's Equity,30000,Equity,FALSE,FALSE,Contributed capital,Active");
            sb.AppendLine("40000,Revenue,,Revenue,FALSE,FALSE,Operating revenue,Active");
            sb.AppendLine("40100,Sales Revenue,40000,Revenue,FALSE,FALSE,Product and service sales,Active");
            sb.AppendLine("50000,Expenses,,Expenses,FALSE,FALSE,Operating expenses,Active");
            sb.AppendLine("50100,Cost of Goods Sold,50000,Expenses,FALSE,FALSE,Direct costs,Active");
            sb.AppendLine("50200,General & Administrative Expenses,50000,Expenses,FALSE,FALSE,Overhead,Active");
            return sb.ToString();
        }

        #endregion

        #region Tree Grouping & Protection Helpers

        private record AccountValidationContext(
            HashSet<int> AccountsWithChildren,
            Dictionary<int, string> GlsRoleMap,
            Dictionary<int, string> TaxMap,
            Dictionary<int, string> BankMap,
            HashSet<int> HistoryAccountIds,
            HashSet<int> ItemCategoryAccountIds,
            HashSet<int> ItemAccountIds,
            HashSet<int> CustomerAccountIds,
            HashSet<int> VendorAccountIds,
            HashSet<int> ContraAccountIds);

        private async Task<AccountValidationContext> LoadAccountValidationContextAsync()
        {
            var accountsWithChildren = new HashSet<int>(await _db.Accounts
                .Where(a => a.ParentAccountId.HasValue)
                .Select(a => a.ParentAccountId!.Value)
                .Distinct()
                .ToListAsync());

            var glsRoleMap = new Dictionary<int, string>();
            var gls = await _db.GeneralLedgerSettings.FirstOrDefaultAsync();
            if (gls != null)
            {
                if (gls.PayableAccountId.HasValue) glsRoleMap[gls.PayableAccountId.Value] = "Payable Account";
                if (gls.PurchaseDiscountAccountId.HasValue) glsRoleMap[gls.PurchaseDiscountAccountId.Value] = "Purchase Discount Account";
                if (gls.GoodsReceiptNoteClearingAccountId.HasValue) glsRoleMap[gls.GoodsReceiptNoteClearingAccountId.Value] = "Goods Received Note Clearing Account";
                if (gls.SalesDiscountAccountId.HasValue) glsRoleMap[gls.SalesDiscountAccountId.Value] = "Sales Discount Account";
                if (gls.ShippingChargeAccountId.HasValue) glsRoleMap[gls.ShippingChargeAccountId.Value] = "Shipping Charge Account";
                if (gls.PermanentAccountId.HasValue) glsRoleMap[gls.PermanentAccountId.Value] = "Permanent (Retained Earnings) Account";
                if (gls.IncomeSummaryAccountId.HasValue) glsRoleMap[gls.IncomeSummaryAccountId.Value] = "Income Summary Account";
            }

            var taxMap = new Dictionary<int, string>();
            var taxes = await _db.Taxes.ToListAsync();
            foreach (var t in taxes)
            {
                if (t.SalesAccountId.HasValue && !taxMap.ContainsKey(t.SalesAccountId.Value))
                    taxMap[t.SalesAccountId.Value] = t.TaxName ?? "Tax";
                if (t.PurchasingAccountId.HasValue && !taxMap.ContainsKey(t.PurchasingAccountId.Value))
                    taxMap[t.PurchasingAccountId.Value] = t.TaxName ?? "Tax";
            }

            var bankMap = new Dictionary<int, string>();
            var banks = await _db.Banks.ToListAsync();
            foreach (var b in banks)
            {
                if (b.AccountId.HasValue && !bankMap.ContainsKey(b.AccountId.Value))
                    bankMap[b.AccountId.Value] = b.Name ?? "Bank";
            }

            var historyAccountIds = new HashSet<int>(await _db.GeneralLedgerLines
                .Select(l => l.AccountId).Distinct().ToListAsync());
            var jeIds = await _db.JournalEntryLines.Select(l => l.AccountId).Distinct().ToListAsync();
            historyAccountIds.UnionWith(jeIds);
            var srDebitIds = await _db.SalesReceiptHeaders.Where(h => h.AccountToDebitId.HasValue).Select(h => h.AccountToDebitId!.Value).Distinct().ToListAsync();
            historyAccountIds.UnionWith(srDebitIds);
            var srCreditIds = await _db.SalesReceiptLines.Where(l => l.AccountToCreditId.HasValue).Select(l => l.AccountToCreditId!.Value).Distinct().ToListAsync();
            historyAccountIds.UnionWith(srCreditIds);
            var silIds = await _db.SalesInvoiceLines.Where(l => l.SalesAccountId.HasValue).Select(l => l.SalesAccountId!.Value).Distinct().ToListAsync();
            historyAccountIds.UnionWith(silIds);
            var pilIds = await _db.PurchaseInvoiceLines.Where(l => l.AccountId.HasValue).Select(l => l.AccountId!.Value).Distinct().ToListAsync();
            historyAccountIds.UnionWith(pilIds);
            var vpIds = await _db.VendorPayments.Where(p => p.AccountId.HasValue).Select(p => p.AccountId!.Value).Distinct().ToListAsync();
            historyAccountIds.UnionWith(vpIds);

            var itemCatIds = new HashSet<int>();
            var cats = await _db.ItemCategories.ToListAsync();
            foreach (var c in cats)
            {
                if (c.AdjustmentAccountId.HasValue) itemCatIds.Add(c.AdjustmentAccountId.Value);
                if (c.AssemblyAccountId.HasValue) itemCatIds.Add(c.AssemblyAccountId.Value);
                if (c.CostOfGoodsSoldAccountId.HasValue) itemCatIds.Add(c.CostOfGoodsSoldAccountId.Value);
                if (c.InventoryAccountId.HasValue) itemCatIds.Add(c.InventoryAccountId.Value);
                if (c.SalesAccountId.HasValue) itemCatIds.Add(c.SalesAccountId.Value);
            }

            var itemIds = new HashSet<int>();
            var items = await _db.Items.ToListAsync();
            foreach (var i in items)
            {
                if (i.InventoryAdjustmentAccountId.HasValue) itemIds.Add(i.InventoryAdjustmentAccountId.Value);
                if (i.CostOfGoodsSoldAccountId.HasValue) itemIds.Add(i.CostOfGoodsSoldAccountId.Value);
                if (i.InventoryAccountId.HasValue) itemIds.Add(i.InventoryAccountId.Value);
                if (i.SalesAccountId.HasValue) itemIds.Add(i.SalesAccountId.Value);
            }

            var custIds = new HashSet<int>();
            var custs = await _db.Customers.ToListAsync();
            foreach (var c in custs)
            {
                if (c.AccountsReceivableAccountId.HasValue) custIds.Add(c.AccountsReceivableAccountId.Value);
                if (c.CustomerAdvancesAccountId.HasValue) custIds.Add(c.CustomerAdvancesAccountId.Value);
                if (c.PromptPaymentDiscountAccountId.HasValue) custIds.Add(c.PromptPaymentDiscountAccountId.Value);
                if (c.SalesAccountId.HasValue) custIds.Add(c.SalesAccountId.Value);
                if (c.SalesDiscountAccountId.HasValue) custIds.Add(c.SalesDiscountAccountId.Value);
            }

            var vendIds = new HashSet<int>();
            var vends = await _db.Vendors.ToListAsync();
            foreach (var v in vends)
            {
                if (v.AccountsPayableAccountId.HasValue) vendIds.Add(v.AccountsPayableAccountId.Value);
                if (v.PurchaseAccountId.HasValue) vendIds.Add(v.PurchaseAccountId.Value);
                if (v.PurchaseDiscountAccountId.HasValue) vendIds.Add(v.PurchaseDiscountAccountId.Value);
            }

            var contraIds = new HashSet<int>();
            var contras = await _db.MainContraAccounts.ToListAsync();
            foreach (var m in contras)
            {
                contraIds.Add(m.MainAccountId);
                contraIds.Add(m.RelatedContraAccountId);
            }

            return new AccountValidationContext(
                accountsWithChildren,
                glsRoleMap,
                taxMap,
                bankMap,
                historyAccountIds,
                itemCatIds,
                itemIds,
                custIds,
                vendIds,
                contraIds);
        }

        private IList<Dto.Financial.Account> BuildAccountGrouping(
            IList<Core.Domain.Financials.Account> allAccounts,
            int? parentAccountId,
            AccountValidationContext? context = null)
        {
            var accountTree = new List<Dto.Financial.Account>();
            var childAccounts = allAccounts.Where(o => o.ParentAccountId == parentAccountId)
                .OrderBy(o => o.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var account in childAccounts)
            {
                bool isProtected = false;
                string? protectionReason = null;
                bool canDelete = false;
                string? deleteBlockedReason = null;

                if (context != null)
                {
                    bool hasChildren = context.AccountsWithChildren.Contains(account.Id);
                    if (hasChildren)
                    {
                        isProtected = true;
                        protectionReason = "Account has child accounts. Reassign or delete child accounts first.";
                        canDelete = false;
                        deleteBlockedReason = protectionReason;
                    }
                    else if (context.GlsRoleMap.TryGetValue(account.Id, out var glsRole))
                    {
                        isProtected = true;
                        protectionReason = $"Account is mapped in General Ledger Settings as {glsRole}. Remap in Settings first.";
                        canDelete = false;
                        deleteBlockedReason = protectionReason;
                    }
                    else if (context.TaxMap.TryGetValue(account.Id, out var taxName))
                    {
                        isProtected = true;
                        protectionReason = $"Account is mapped in Tax Settings for tax rate '{taxName}'. Remap in Tax Settings first.";
                        canDelete = false;
                        deleteBlockedReason = protectionReason;
                    }
                    else if (context.BankMap.TryGetValue(account.Id, out var bankName))
                    {
                        isProtected = true;
                        protectionReason = $"Account is linked as GL account for Bank '{bankName}'. Remap bank account in Banking first.";
                        canDelete = false;
                        deleteBlockedReason = protectionReason;
                    }
                    else if (context.HistoryAccountIds.Contains(account.Id))
                    {
                        canDelete = false;
                        deleteBlockedReason = "Cannot delete account because it has transaction history in the general ledger. Use Archive instead.";
                    }
                    else if (context.ItemCategoryAccountIds.Contains(account.Id))
                    {
                        canDelete = false;
                        deleteBlockedReason = "Cannot delete account because it is used by one or more item categories. Reassign item categories first.";
                    }
                    else if (context.ItemAccountIds.Contains(account.Id))
                    {
                        canDelete = false;
                        deleteBlockedReason = "Cannot delete account because it is used by one or more items. Reassign items first.";
                    }
                    else if (context.CustomerAccountIds.Contains(account.Id))
                    {
                        canDelete = false;
                        deleteBlockedReason = "Cannot delete account because it is configured on one or more customers. Unmap from customers first.";
                    }
                    else if (context.VendorAccountIds.Contains(account.Id))
                    {
                        canDelete = false;
                        deleteBlockedReason = "Cannot delete account because it is configured on one or more vendors. Unmap from vendors first.";
                    }
                    else if (context.ContraAccountIds.Contains(account.Id))
                    {
                        canDelete = false;
                        deleteBlockedReason = "Cannot delete account because it is linked in contra accounts. Remove the contra link first.";
                    }
                    else
                    {
                        canDelete = true;
                        deleteBlockedReason = null;
                    }
                }

                var accountDto = new Dto.Financial.Account()
                {
                    Id = account.Id,
                    AccountClassId = account.AccountClassId,
                    ParentAccountId = account.ParentAccountId,
                    CompanyId = account.CompanyId,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    Description = account.Description,
                    IsCash = account.IsCash,
                    IsContraAccount = account.IsContraAccount,
                    Archived = account.ArchivedAt.HasValue,
                    ArchivedAt = account.ArchivedAt,
                    IsProtected = isProtected,
                    ProtectionReason = protectionReason,
                    CanDelete = canDelete,
                    DeleteBlockedReason = deleteBlockedReason,
                    NormalBalance = account.DrOrCrSide.ToString(),
                    AccountClassName = account.AccountClass?.Name ?? (CLASS_NAMES.TryGetValue(account.AccountClassId, out var cn) ? cn : null),
                    CanPost = account.CanPost(),
                    Balance = account.Balance,
                    DebitBalance = account.DebitBalance,
                    CreditBalance = account.CreditBalance
                };
                var children = BuildAccountGrouping(allAccounts, account.Id, context);
                accountDto.ChildAccounts = children;
                accountTree.Add(accountDto);
            }

            return accountTree;
        }

        #endregion

        #region Bank Management
        [HttpGet]
        [Route("Bank/{id}")] // api/Financials/Bank/1
        public IActionResult GetBank(int id)
        {
            try
            {
                var bank = _bankAccountService.Get(id, includeFullNumbers: false);
                return Ok(ToLegacyBankDto(bank));
            }
            catch (Core.Tenancy.TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank not found" });
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException or Services.Banking.ReconciledPeriodConfirmationRequiredException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Route("Bank")] // api/Financials/Bank
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult CreateBank([FromBody] Dto.Financial.Bank bankDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                using var tx = RelationalDatabaseFacadeExtensions.BeginTransaction(_db.Database, System.Data.IsolationLevel.Serializable);
                var created = _bankAccountService.Create(new CreateBankAccountRequest
                {
                    Name = string.IsNullOrWhiteSpace(bankDto.Name) ? (bankDto.BankName ?? "Bank account") : bankDto.Name!,
                    BankName = bankDto.BankName,
                    AccountNumber = bankDto.AccountNo,
                    PostingAccountId = bankDto.AccountId,
                    Type = (int)Core.Domain.BankTypes.CheckingAccount
                }, includeFullNumbers: false);
                tx.Commit();

                return CreatedAtAction(nameof(GetBank), new { id = created.Id }, ToLegacyBankDto(created));
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException or Services.Banking.ReconciledPeriodConfirmationRequiredException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut]
        [Route("Bank/{id}")] // api/Financials/Bank/1
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult UpdateBank(int id, [FromBody] Dto.Financial.Bank bankDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                using var tx = RelationalDatabaseFacadeExtensions.BeginTransaction(_db.Database, System.Data.IsolationLevel.Serializable);
                var existing = _bankAccountService.Get(id, includeFullNumbers: true);
                var updated = _bankAccountService.Update(id, new UpdateBankAccountRequest
                {
                    Name = string.IsNullOrWhiteSpace(bankDto.Name) ? existing.Name : bankDto.Name!,
                    Type = existing.Type,
                    BankName = bankDto.BankName,
                    AccountNumber = bankDto.AccountNo,
                    RoutingNumber = existing.RoutingNumber,
                    Iban = existing.Iban,
                    Swift = existing.Swift,
                    IsDefaultForReceipts = existing.IsDefaultForReceipts,
                    IsDefaultForPayments = existing.IsDefaultForPayments,
                    PostingAccountId = bankDto.AccountId,
                    CurrencyCode = existing.CurrencyCode
                }, includeFullNumbers: false);
                tx.Commit();

                return Ok(ToLegacyBankDto(updated));
            }
            catch (Core.Tenancy.TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank not found" });
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException or Services.Banking.ReconciledPeriodConfirmationRequiredException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete]
        [Route("Bank/{id}")] // api/Financials/Bank/1
        [RequireModule("financials")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult DeleteBank(int id)
        {
            try
            {
                using var tx = RelationalDatabaseFacadeExtensions.BeginTransaction(_db.Database, System.Data.IsolationLevel.Serializable);
                var archived = _bankAccountService.DeleteOrArchive(id);
                tx.Commit();
                return Ok(new
                {
                    message = archived ? "Bank archived successfully" : "Bank deleted successfully"
                });
            }
            catch (Core.Tenancy.TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank not found" });
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException or Services.Banking.ReconciledPeriodConfirmationRequiredException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        private static Bank ToLegacyBankDto(BankAccountDto bank) => new()
        {
            Id = bank.Id,
            Name = bank.Name,
            AccountNo = bank.AccountNumberMasked ?? bank.AccountNumber,
            BankName = bank.BankName,
            AccountId = bank.AccountId
        };
        #endregion
    }
}