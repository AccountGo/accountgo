// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Banking
{
    /// <summary>Plain-language expense category for Record expense (#436 I2).</summary>
    public class StatementExpenseCategoryDto
    {
        public int AccountId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>True when the account is on the short curated list; false for "More…".</summary>
        public bool Curated { get; set; }
    }

    public class StatementExpenseCategoriesDto
    {
        public IReadOnlyList<StatementExpenseCategoryDto> Curated { get; set; } = Array.Empty<StatementExpenseCategoryDto>();
        public IReadOnlyList<StatementExpenseCategoryDto> More { get; set; } = Array.Empty<StatementExpenseCategoryDto>();
    }

    public class QuickVendorFromStatementRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    /// <summary>Record a money-out statement line as a paid one-line bill (#436 I2).</summary>
    public class RecordExpenseFromStatementLineRequest
    {
        /// <summary>Existing vendor, or omit and set <see cref="NewVendor"/>.</summary>
        public int? VendorId { get; set; }
        public QuickVendorFromStatementRequest? NewVendor { get; set; }
        public int ExpenseAccountId { get; set; }
        public int? TaxId { get; set; }
        public string? Description { get; set; }
        public string? VendorInvoiceNo { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Memo { get; set; }
    }

    /// <summary>Apply a money-out statement line as payment on an open bill (#436 I2).</summary>
    public class PayOpenBillFromStatementLineRequest
    {
        public int BillId { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Memo { get; set; }
    }

    public class StatementLineDocumentLinkDto
    {
        /// <summary>bill | vendorPayment (and later invoice / salesReceipt).</summary>
        public string Kind { get; set; } = string.Empty;
        public int Id { get; set; }
        public string? No { get; set; }
    }

    public class RecordExpenseFromStatementLineResultDto
    {
        public BankStatementLineDto Line { get; set; } = new();
        public StatementLineDocumentLinkDto Bill { get; set; } = new();
        public StatementLineDocumentLinkDto Payment { get; set; } = new();
    }

    public class PayOpenBillFromStatementLineResultDto
    {
        public BankStatementLineDto Line { get; set; } = new();
        public StatementLineDocumentLinkDto Bill { get; set; } = new();
        public StatementLineDocumentLinkDto Payment { get; set; } = new();
    }
}
