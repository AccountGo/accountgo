// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Banking
{
    public class StatementIncomeCategoryDto
    {
        public int AccountId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Curated { get; set; }
    }

    public class StatementIncomeCategoriesDto
    {
        public IReadOnlyList<StatementIncomeCategoryDto> Curated { get; set; } = Array.Empty<StatementIncomeCategoryDto>();
        public IReadOnlyList<StatementIncomeCategoryDto> More { get; set; } = Array.Empty<StatementIncomeCategoryDto>();
    }

    public class RecordOtherIncomeFromStatementLineRequest
    {
        /// <summary>Optional. When omitted, the workspace "Other income" customer is used.</summary>
        public int? CustomerId { get; set; }
        public int IncomeAccountId { get; set; }
        public int? TaxId { get; set; }
        public string? Description { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Memo { get; set; }
    }

    public class RecordOtherIncomeFromStatementLineResultDto
    {
        public BankStatementLineDto Line { get; set; } = new();
        /// <summary>kind is "invoice" for v1; a later sales-receipt PR may return "salesReceipt".</summary>
        public StatementLineDocumentLinkDto Document { get; set; } = new();
        public StatementLineDocumentLinkDto Receipt { get; set; } = new();
    }
}
