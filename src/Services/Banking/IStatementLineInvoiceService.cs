// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Dto.Banking;

namespace Services.Banking
{
    /// <summary>
    /// Apply unmatched money-in statement lines to open invoices through the normal receipt path (#436 I3).
    /// Never posts to the GL directly.
    /// </summary>
    public interface IStatementLineInvoiceService
    {
        ApplyOpenInvoiceFromStatementLineResultDto ApplyOpenInvoice(
            int lineId,
            ApplyOpenInvoiceFromStatementLineRequest request,
            string actor);
    }
}
