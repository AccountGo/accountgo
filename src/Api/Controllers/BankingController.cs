// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data;
using System.Globalization;
using Api.Data;
using Api.Filters;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services.Banking;

namespace Api.Controllers
{
    [TenantScoped]
    [Route("api/[controller]")]
    [RequireModule(SystemModules.Sales, SystemModules.Purchases, SystemModules.Financials)]
    public class BankingController : BaseController
    {
        private readonly IBankAccountService _banks;
        private readonly IBankTransactionService _transactions;
        private readonly IBankReconciliationService _reconciliations;
        private readonly ITenantContext _tenant;
        private readonly IEntitlementService _entitlements;
        private readonly ApiDbContext _db;

        public BankingController(
            IBankAccountService banks,
            IBankTransactionService transactions,
            IBankReconciliationService reconciliations,
            ITenantContext tenant,
            IEntitlementService entitlements,
            ApiDbContext db)
        {
            _banks = banks;
            _transactions = transactions;
            _reconciliations = reconciliations;
            _tenant = tenant;
            _entitlements = entitlements;
            _db = db;
        }

        [HttpGet("Accounts")]
        [RequirePermission(SystemPermissions.BankingAccountsView)]
        public IActionResult ListAccounts([FromQuery] bool includeArchived = false)
        {
            return Ok(_banks.List(
                includeArchived,
                includeFullNumbers: CanManage(),
                includeCashHistoryCodes: HasFinancialsModule()));
        }

        [HttpGet("Accounts/Picker")]
        public IActionResult Picker()
        {
            if (!CanPickBanks())
                return RequirePermissionAttribute.Denied(SystemPermissions.BankingAccountsView);

            var includeOther = CanCreatePayments() && HasFinancialsModule();
            return Ok(_banks.Picker(includeOther));
        }

        [HttpGet("Accounts/CashHistory")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult CashHistory()
        {
            return Ok(_banks.GetCashHistory(includeCodes: HasFinancialsModule()));
        }

        [HttpGet("Accounts/EligiblePostingAccounts")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult EligiblePostingAccounts()
        {
            return Ok(_banks.EligiblePostingAccounts());
        }

        [HttpGet("Accounts/{id:int}")]
        [RequirePermission(SystemPermissions.BankingAccountsView)]
        public IActionResult GetAccount(int id)
        {
            try
            {
                return Ok(_banks.Get(id, includeFullNumbers: CanManage()));
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
        }

        [HttpGet("Accounts/{id:int}/Register")]
        [RequirePermission(SystemPermissions.BankingAccountsView)]
        public IActionResult GetRegister(
            int id,
            [FromQuery] string? from = null,
            [FromQuery] string? to = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = BankAccountService.RegisterDefaultPageSize)
        {
            try
            {
                return Ok(_banks.GetRegister(id, ParseRegisterDate(from), ParseRegisterDate(to), page, pageSize));
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private static DateTime? ParseRegisterDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed.Date;
            throw new FormatException($"Invalid date '{value}'. Use yyyy-MM-dd.");
        }

        [HttpPost("Accounts")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult CreateAccount([FromBody] CreateBankAccountRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var created = _banks.Create(request, includeFullNumbers: true);
                tx.Commit();
                return CreatedAtAction(nameof(GetAccount), new { id = created.Id }, created);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("Accounts/{id:int}")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult UpdateAccount(int id, [FromBody] UpdateBankAccountRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var updated = _banks.Update(id, request, includeFullNumbers: true);
                tx.Commit();
                return Ok(updated);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Accounts/{id:int}/Archive")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult Archive(int id, [FromQuery] bool confirm = false)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var archived = _banks.Archive(id, includeFullNumbers: true, confirmNonZeroBalance: confirm);
                tx.Commit();
                return Ok(archived);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (BankArchiveConfirmationRequiredException ex)
            {
                return Conflict(new
                {
                    code = "ARCHIVE_CONFIRM_REQUIRED",
                    message = ex.Message,
                    balance = ex.Balance
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Accounts/{id:int}/Unarchive")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult Unarchive(int id)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var restored = _banks.Unarchive(id, includeFullNumbers: true);
                tx.Commit();
                return Ok(restored);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Accounts/{id:int}/SetDefault")]
        [RequirePermission(SystemPermissions.BankingAccountsManage)]
        public IActionResult SetDefault(int id, [FromQuery(Name = "for")] string forKind)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var updated = _banks.SetDefault(id, forKind, includeFullNumbers: true);
                tx.Commit();
                return Ok(updated);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("Transactions")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingAccountsView)]
        public IActionResult ListTransactions(
            [FromQuery] int bankId,
            [FromQuery] string? from = null,
            [FromQuery] string? to = null)
        {
            try
            {
                return Ok(new BankTransactionListDto
                {
                    Transactions = _transactions.List(bankId, ParseRegisterDate(from), ParseRegisterDate(to))
                });
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Transactions")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingTransactionsCreate)]
        public IActionResult CreateTransaction([FromBody] CreateBankTransactionRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var created = _transactions.Create(request);
                tx.Commit();
                return Ok(created);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Transactions/{id:int}/Void")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingTransactionsCreate)]
        public IActionResult VoidTransaction(int id, [FromBody] VoidBankTransactionRequest? request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var voided = _transactions.Void(id, request ?? new VoidBankTransactionRequest());
                tx.Commit();
                return Ok(voided);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank transaction not found" });
            }
            catch (ReconciledTransactionException ex)
            {
                return ReconciledConflict(ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("Reconciliations")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingAccountsView)]
        public IActionResult ListReconciliations([FromQuery] int bankId)
        {
            try
            {
                return Ok(_reconciliations.List(bankId));
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Reconciliations")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingReconcile)]
        public IActionResult StartReconciliation([FromBody] StartBankReconciliationRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var created = _reconciliations.Start(request, GetUserNameFromRequestHeader() ?? string.Empty);
                tx.Commit();
                return Ok(created);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Bank account not found" });
            }
            catch (BankReconciliationConflictException ex)
            {
                return ReconciliationConflict(ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("Reconciliations/{id:int}")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingAccountsView)]
        public IActionResult GetReconciliation(int id)
        {
            try
            {
                return Ok(_reconciliations.Get(id));
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Reconciliation not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("Reconciliations/{id:int}/Items")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingReconcile)]
        public IActionResult UpdateReconciliationItems(int id, [FromBody] UpdateBankReconciliationItemsRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var updated = _reconciliations.UpdateItems(id, request);
                tx.Commit();
                return Ok(updated);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Reconciliation not found" });
            }
            catch (BankReconciliationConflictException ex)
            {
                return ReconciliationConflict(ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Reconciliations/{id:int}/Adjustments")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingReconcile)]
        [RequirePermission(SystemPermissions.BankingTransactionsCreate)]
        public IActionResult AddReconciliationAdjustment(int id, [FromBody] CreateReconciliationAdjustmentRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var updated = _reconciliations.AddAdjustment(id, request);
                tx.Commit();
                return Ok(updated);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Reconciliation not found" });
            }
            catch (BankReconciliationConflictException ex)
            {
                return ReconciliationConflict(ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Reconciliations/{id:int}/Finish")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingReconcile)]
        public IActionResult FinishReconciliation(int id, [FromBody] FinishBankReconciliationRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var finished = _reconciliations.Finish(id, request, GetUserNameFromRequestHeader() ?? string.Empty);
                tx.Commit();
                return Ok(finished);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Reconciliation not found" });
            }
            catch (BankReconciliationConflictException ex)
            {
                return ReconciliationConflict(ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Reconciliations/{id:int}/Undo")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingReconcileUndo)]
        public IActionResult UndoReconciliation(int id, [FromBody] UndoBankReconciliationRequest request)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                var undone = _reconciliations.Undo(id, request, GetUserNameFromRequestHeader() ?? string.Empty);
                tx.Commit();
                return Ok(undone);
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Reconciliation not found" });
            }
            catch (BankReconciliationConflictException ex)
            {
                return ReconciliationConflict(ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("Reconciliations/{id:int}")]
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingReconcile)]
        public IActionResult DiscardReconciliation(int id)
        {
            try
            {
                using var tx = BeginWriteTransaction();
                _reconciliations.DiscardDraft(id);
                tx.Commit();
                return NoContent();
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(new { message = "Reconciliation not found" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private static IActionResult ReconciliationConflict(BankReconciliationConflictException ex) =>
            new ConflictObjectResult(new
            {
                code = ex.Code,
                message = ex.Message,
                reconciliationId = ex.ReconciliationId
            });

        private static IActionResult ReconciledConflict(ReconciledTransactionException ex) =>
            new ConflictObjectResult(new
            {
                code = ReconciledTransactionException.ErrorCode,
                message = ex.Message,
                bankName = ex.BankName,
                statementEndDate = ex.StatementEndDate.ToString("yyyy-MM-dd")
            });

        private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BeginWriteTransaction() =>
            RelationalDatabaseFacadeExtensions.BeginTransaction(_db.Database, IsolationLevel.Serializable);

        private bool CanManage() =>
            _tenant.IsResolved && _tenant.Permissions.Contains(SystemPermissions.BankingAccountsManage);

        private bool CanPickBanks() =>
            _tenant.IsResolved && (
                _tenant.Permissions.Contains(SystemPermissions.SalesReceiptsCreate)
                || _tenant.Permissions.Contains(SystemPermissions.PurchasesPaymentsCreate)
                || _tenant.Permissions.Contains(SystemPermissions.BankingAccountsView));

        private bool CanCreatePayments() =>
            _tenant.IsResolved && _tenant.Permissions.Contains(SystemPermissions.PurchasesPaymentsCreate);

        private bool HasFinancialsModule()
        {
            if (!_tenant.IsResolved)
                return false;
            var decision = _entitlements.Evaluate(
                _tenant.RequireCompanyId(),
                new EntitlementAction.Module(ModuleAccess.Read, SystemModules.Financials));
            return decision.Allowed;
        }
    }
}
