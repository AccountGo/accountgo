// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Dto.Banking
{
    /// <summary>Apply a money-in statement line as payment on an open invoice (#436 I3).</summary>
    public class ApplyOpenInvoiceFromStatementLineRequest
    {
        public int InvoiceId { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Memo { get; set; }
    }

    public class ApplyOpenInvoiceFromStatementLineResultDto
    {
        public BankStatementLineDto Line { get; set; } = new();
        public StatementLineDocumentLinkDto Invoice { get; set; } = new();
        public StatementLineDocumentLinkDto Receipt { get; set; } = new();
    }
}
