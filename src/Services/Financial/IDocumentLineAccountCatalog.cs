// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using Core.Domain.Financials;

namespace Services.Financial
{
    /// <summary>
    /// Postable accounts for bill expense lines and (later) other-income invoice lines.
    /// Shared by Purchasing BillAccounts and statement import (#436).
    /// </summary>
    public interface IDocumentLineAccountCatalog
    {
        /// <summary>
        /// Leaf expense/asset accounts that are not archived, not contra, not cash or cash-child,
        /// not 10120, and not a customer AR control account.
        /// </summary>
        IReadOnlyList<Account> ListBillLineAccounts();

        void EnsureBillLineAccount(int accountId);
    }
}
