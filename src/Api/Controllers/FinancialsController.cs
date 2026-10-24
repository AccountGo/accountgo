using Api.Filters;
using Core.Domain.Security;
using Dto.Financial;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Financial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    [RequireModule("financials", "reports")]
    //[Authorize]
    public class FinancialsController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;
        private readonly IAccountService _accountService;
        private readonly Core.Tenancy.ITenantGuard _tenantGuard;

        public FinancialsController(IAdministrationService adminService,
            IFinancialService financialService,
            IAccountService accountService,
            Core.Tenancy.ITenantGuard tenantGuard)
        {
            _adminService = adminService;
            _financialService = financialService;
            _accountService = accountService;
            _tenantGuard = tenantGuard;
        }

        [HttpGet]
        [Route("CashBanks")] // api/Financials/CashBanks
        public IActionResult CashBanks()
        {
            var cashAndBanks = _financialService.GetCashAndBanks();
            var cashAndBanksDto = new List<Bank>();

            foreach (var bank in cashAndBanks)
            {
                cashAndBanksDto.Add(new Bank()
                {
                    Id = bank.Id,
                    Name = bank.Name,
                    AccountNo = bank.Number,
                    BankName = bank.BankName,
                    AccountId = bank.AccountId
                });
            }

            return new ObjectResult(cashAndBanksDto);
        }

        [HttpGet]
        [Route("Accounts")] // api/Financials/Accounts
        public IActionResult Accounts()
        {
            var accounts = _financialService.GetAccounts().ToList();

            var accountTree = BuildAccountGrouping(accounts, null);

            return new ObjectResult(accountTree);
        }

        [HttpGet]
        [Route("Account")]
        public IActionResult Account(int id)
        {
            var account = _financialService.GetAccount(id);
            if (account == null)
                return NotFound(new { message = $"Account #{id} not found." });

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
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;

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
                if (ex is Core.Tenancy.TenantException) throw;
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
                if (ex is Core.Tenancy.TenantException) throw;
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

        #region CRUD for Accounts

        [HttpGet]
        [Route("GetAccount/{accountCode}")]
        public async Task<IActionResult> GetAccountByCode(string accountCode)
        {
            var account = await _accountService.GetAccountByCodeAsync(accountCode);
            if (account == null)
                return NotFound();

            // Return a DTO without accessing computed properties that require navigation properties
            // These computed properties would cause NullReferenceException if navigation properties aren't loaded
            return Ok(new Account
            {
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                Balance = 0, // Computed property - would require AccountClass and GeneralLedgerLines to be loaded
                DebitBalance = 0, // Computed property - would require GeneralLedgerLines to be loaded
                CreditBalance = 0 // Computed property - would require GeneralLedgerLines to be loaded
            });
        }

        [HttpPost]
        [Route("AddAccount")]
        [RequireModule("financials")]
        public async Task<IActionResult> AddAccount([FromBody] Account newAccountDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(newAccountDto.ParentAccountId, nameof(newAccountDto.ParentAccountId));

            var newAccount = new Core.Domain.Financials.Account
            {
                AccountCode = newAccountDto.AccountCode,
                AccountName = newAccountDto.AccountName,
                ParentAccountId = newAccountDto.ParentAccountId
                // Balance = 0, // Initialize read-only fields
                // DebitBalance = 0,
                // CreditBalance = 0
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

            // Return a DTO instead of the domain object to avoid serialization issues with computed properties
            var accountDto = new Account
            {
                AccountCode = createdAccount.AccountCode,
                AccountName = createdAccount.AccountName,
                Balance = 0, // These would require navigation properties to be loaded
                DebitBalance = 0,
                CreditBalance = 0
            };

            return CreatedAtAction(nameof(GetAccountByCode), new { accountCode = createdAccount.AccountCode }, accountDto);
        }

        [HttpPut]
        [Route("UpdateAccount/{accountCode}")]
        [RequireModule("financials")]
        public async Task<IActionResult> UpdateAccount(string accountCode, [FromBody] Account updatedAccountDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            Core.Domain.Financials.Account? updatedAccount;
            try
            {
                updatedAccount = await _accountService.UpdateAccountAsync(accountCode, new Core.Domain.Financials.Account
                {
                    AccountCode = updatedAccountDto.AccountCode,
                    AccountName = updatedAccountDto.AccountName,
                    // Balance = updatedAccountDto.Balance,
                    // DebitBalance = updatedAccountDto.DebitBalance,
                    // CreditBalance = updatedAccountDto.CreditBalance
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            if (updatedAccount == null)
                return NotFound();

            // Return a DTO instead of the domain object to avoid serialization issues with computed properties
            // that require navigation properties to be loaded
            return Ok(new Account
            {
                AccountCode = updatedAccount.AccountCode,
                AccountName = updatedAccount.AccountName,
                Balance = 0, // These would require navigation properties to be loaded
                DebitBalance = 0,
                CreditBalance = 0
            });
        }

        [HttpDelete]
        [Route("DeleteAccount/{accountCode}")]
        [RequireModule("financials")]
        public async Task<IActionResult> DeleteAccount(string accountCode)
        {
            var (account, errorMessage) = await _accountService.DeleteAccountAsync(accountCode);

            if (account == null)
            {
                if (errorMessage == "Account not found")
                    return NotFound(errorMessage);
                else
                    return BadRequest(errorMessage);
            }

            // Return NoContent (204) for successful DELETE operations
            // This is the standard HTTP response for DELETE operations
            return NoContent();
        }

        #endregion

        #region 
        private IList<Dto.Financial.Account> BuildAccountGrouping(IList<Core.Domain.Financials.Account> allAccounts,
        int? parentAccountId)
        {
            var accountTree = new List<Dto.Financial.Account>();
            var childAccounts = allAccounts.Where(o => o.ParentAccountId == parentAccountId).ToList();

            foreach (var account in childAccounts)
            {
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
                    NormalBalance = account.DrOrCrSide.ToString(),
                    AccountClassName = account.AccountClass?.Name,
                    CanPost = account.CanPost(),
                    Balance = account.Balance,
                    DebitBalance = account.DebitBalance,
                    CreditBalance = account.CreditBalance
                };
                var children = BuildAccountGrouping(allAccounts, account.Id);
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
                var bank = _financialService.GetBank(id);
                if (bank == null)
                    return NotFound(new { message = "Bank not found" });

                var bankDto = new Dto.Financial.Bank()
                {
                    Id = bank.Id,
                    Name = bank.Name,
                    AccountNo = bank.Number,
                    BankName = bank.BankName,
                    AccountId = bank.AccountId
                };

                return Ok(bankDto);
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Route("Bank")] // api/Financials/Bank
        [RequireModule("financials")]
        public IActionResult CreateBank([FromBody] Dto.Financial.Bank bankDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(bankDto.AccountId, nameof(bankDto.AccountId));
                var account = GetEligibleBankAccount(bankDto.AccountId);
                if (account == null)
                    return BadRequest(new { message = "Select an account with a code between 10100 and 10999." });

                var bank = new Core.Domain.Financials.Bank()
                {
                    Name = account.AccountName,
                    BankName = bankDto.BankName,
                    Number = bankDto.AccountNo,
                    AccountId = bankDto.AccountId,
                    Type = Core.Domain.BankTypes.CheckingAccount,
                    IsActive = true
                };

                _financialService.SaveBank(bank);

                var result = new Dto.Financial.Bank()
                {
                    Id = bank.Id,
                    Name = bank.Name,
                    AccountNo = bank.Number,
                    BankName = bank.BankName,
                    AccountId = bank.AccountId
                };

                return CreatedAtAction(nameof(GetBank), new { id = bank.Id }, result);
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut]
        [Route("Bank/{id}")] // api/Financials/Bank/1
        [RequireModule("financials")]
        public IActionResult UpdateBank(int id, [FromBody] Dto.Financial.Bank bankDto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                _tenantGuard.EnsureOwned<Core.Domain.Financials.Account>(bankDto.AccountId, nameof(bankDto.AccountId));
                var account = GetEligibleBankAccount(bankDto.AccountId);
                if (account == null)
                    return BadRequest(new { message = "Select an account with a code between 10100 and 10999." });

                var bank = _tenantGuard.EnsureOwned<Core.Domain.Financials.Bank>(id, nameof(id));

                bank.Name = account.AccountName;
                bank.BankName = bankDto.BankName;
                bank.Number = bankDto.AccountNo;
                bank.AccountId = bankDto.AccountId;

                _financialService.SaveBank(bank);

                var result = new Dto.Financial.Bank()
                {
                    Id = bank.Id,
                    Name = bank.Name,
                    AccountNo = bank.Number,
                    BankName = bank.BankName,
                    AccountId = bank.AccountId
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete]
        [Route("Bank/{id}")] // api/Financials/Bank/1
        [RequireModule("financials")]
        public IActionResult DeleteBank(int id)
        {
            try
            {
                var bank = _financialService.GetBank(id);
                if (bank == null)
                    return NotFound(new { message = "Bank not found" });

                _financialService.DeleteBank(id);

                return Ok(new { message = "Bank deleted successfully" });
            }
            catch (Exception ex)
            {
                if (ex is Core.Tenancy.TenantException) throw;
                return BadRequest(new { message = ex.Message });
            }
        }

        private Core.Domain.Financials.Account? GetEligibleBankAccount(int? accountId)
        {
            if (!accountId.HasValue)
                return null;

            var account = _financialService.GetAccounts().FirstOrDefault(a => a.Id == accountId.Value);
            int accountCode;
            if (account == null
                || !int.TryParse(account.AccountCode, out accountCode)
                || accountCode < 10100
                || accountCode > 10999)
                return null;

            return account;
        }
        #endregion
    }
}