// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Domain;

namespace Services.Banking
{
    /// <summary>
    /// Shared D4 cash-account code allocator for bank accounts (#413).
    /// Prefers 10114–10119, then the lowest free code in 10100–10999 that the
    /// active COA template does not reserve. Used by schema clean-up (1A) and
    /// <c>BankAccountService</c> (1B).
    /// </summary>
    public static class BankCashAccountCodeAllocator
    {
        public const int CashCodeMin = 10100;
        public const int CashCodeMax = 10999;
        public const int PreferredCodeMin = 10114;
        public const int PreferredCodeMax = 10119;

        public static string Allocate(IEnumerable<string> usedAccountCodes, string? coaTemplateId = null)
        {
            ArgumentNullException.ThrowIfNull(usedAccountCodes);

            var unavailable = new HashSet<string>(usedAccountCodes, StringComparer.Ordinal);
            foreach (var reserved in CoaTemplates.ReservedCashRangeAccountCodes(
                         string.IsNullOrWhiteSpace(coaTemplateId) ? CoaTemplates.Legacy : coaTemplateId))
            {
                unavailable.Add(reserved);
            }

            for (var code = PreferredCodeMin; code <= PreferredCodeMax; code++)
            {
                var text = code.ToString(CultureInfo.InvariantCulture);
                if (!unavailable.Contains(text))
                    return text;
            }

            for (var code = CashCodeMin; code <= CashCodeMax; code++)
            {
                var text = code.ToString(CultureInfo.InvariantCulture);
                if (!unavailable.Contains(text))
                    return text;
            }

            throw new InvalidOperationException(
                "No free account code left in 10100–10999 for a dedicated bank cash account.");
        }
    }
}
