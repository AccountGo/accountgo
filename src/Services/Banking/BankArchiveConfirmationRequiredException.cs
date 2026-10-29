// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;

namespace Services.Banking
{
    /// <summary>
    /// I&amp;B workspaces must confirm before archiving a bank with a non-zero book balance (D9).
    /// </summary>
    public sealed class BankArchiveConfirmationRequiredException : InvalidOperationException
    {
        public decimal Balance { get; }

        public BankArchiveConfirmationRequiredException(decimal balance)
            : base(
                $"This account has a balance of {balance.ToString("0.00", CultureInfo.InvariantCulture)}. Confirm to archive it.")
        {
            Balance = balance;
        }
    }
}
