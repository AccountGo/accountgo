// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data;
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
        private readonly ITenantContext _tenant;
        private readonly IEntitlementService _entitlements;
        private readonly ApiDbContext _db;

        public BankingController(
            IBankAccountService banks,
            ITenantContext tenant,
            IEntitlementService entitlements,
            ApiDbContext db)
        {
            _banks = banks;
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
