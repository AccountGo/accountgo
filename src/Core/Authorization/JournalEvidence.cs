// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Domain.Auditing;

namespace Core.Authorization
{
    public static class JournalEvidence
    {
        public static HashSet<string> PreparerIds(IQueryable<AuditLog> rows, int companyId, int journalId)
        {
            var recordId = journalId.ToString(CultureInfo.InvariantCulture);
            return rows.Where(a => a.CompanyId == companyId && a.TableName == "__JournalEntryPreparer" && a.RecordId == recordId)
                .Select(a => a.UserName).Distinct().ToHashSet(StringComparer.Ordinal);
        }
    }
}
