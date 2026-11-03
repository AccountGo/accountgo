// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Linq;
using Api.Controllers;
using Api.Data;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Entitlements;
using Dto.Banking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Services.Banking;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementReviewTransactionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Draft_decision_and_reviewer_audit_commit_or_rollback_together(bool failDecision, bool failAudit)
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        var tenant = new TenantContext();
        tenant.SetTenant(1, "test", CompanyMemberRole.Admin, Array.Empty<string>(), "reviewer-id");
        using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection)
            .AddInterceptors(new RejectAudit(failAudit)).Options, tenant);
        db.Database.EnsureCreated();
        var bank = new Bank { CompanyId = 1, Name = "Bank", CurrencyCode = "USD", AccountId = 99, IsActive = true };
        db.Banks.Add(bank);
        db.SaveChanges();
        var import = new BankStatementImport { CompanyId = 1, BankId = bank.Id, Source = BankStatementSources.Webhook };
        db.BankStatementImports.Add(import);
        db.SaveChanges();
        var line = new BankStatementLine
        { CompanyId = 1, BankId = bank.Id, BankStatementImportId = import.Id, Amount = -50, PostedDate = DateTime.UtcNow.Date };
        db.BankStatementLines.Add(line);
        db.SaveChanges();
        var expenses = new Mock<IStatementLineExpenseService>();
        expenses.Setup(e => e.RecordExpense(line.Id, It.IsAny<RecordExpenseFromStatementLineRequest>(), It.IsAny<string>(), true))
            .Returns(() =>
            {
                line.DraftBillId = 123;
                db.SaveChanges();
                if (failDecision) throw new InvalidOperationException("Decision failed.");
                return new RecordExpenseFromStatementLineResultDto { Bill = new() { Id = 123 } };
            });
        var controller = new BankingController(Mock.Of<IBankAccountService>(), Mock.Of<IBankTransactionService>(),
            Mock.Of<IBankReconciliationService>(), Mock.Of<IBankStatementImportService>(), expenses.Object,
            Mock.Of<IStatementLineInvoiceService>(), Mock.Of<IStatementIncomeRecorder>(), tenant,
            Mock.Of<IEntitlementService>(), Mock.Of<IBooksLockGuard>(), db)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

        var result = controller.DraftExpenseFromStatementLine(line.Id, new());

        var persisted = db.BankStatementLines.AsNoTracking().Single();
        if (failDecision || failAudit)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Null(persisted.DraftBillId);
            Assert.Empty(db.AuditLogs.AsNoTracking());
        }
        else
        {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(123, persisted.DraftBillId);
            var audit = Assert.Single(db.AuditLogs.AsNoTracking());
            Assert.Equal("reviewer-id", audit.UserName);
            Assert.Equal(line.Id.ToString(), audit.RecordId);
            Assert.Contains("save_draft", audit.NewValue);
            Assert.Contains("123", audit.NewValue);
        }
        Assert.Null(persisted.MatchedGeneralLedgerLineId);
        Assert.Empty(db.VendorPayments);
        Assert.Empty(db.GeneralLedgerHeaders);
    }

    private sealed class RejectAudit(bool reject) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (reject && eventData.Context!.ChangeTracker.Entries<AuditLog>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Audit is unavailable.");
            return result;
        }
    }
}
