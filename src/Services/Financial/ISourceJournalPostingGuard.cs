// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
namespace Services.Financial
{
    /// <summary>
    /// Reason a source-referenced journal entry cannot be posted.
    /// </summary>
    public sealed record SourceJournalPostingBlock(string Code, string Message);

    /// <summary>
    /// Optional module guard that can block posting a source-referenced journal entry
    /// when its source state is stale, conflicted, or pending recovery.
    /// </summary>
    public interface ISourceJournalPostingGuard
    {
        /// <summary>
        /// Returns null if the source journal may be posted, or a block reason if posting must be refused.
        /// </summary>
        SourceJournalPostingBlock? CheckPostingAllowed(int companyId, string sourceSystem, string? sourceKey, int journalId);
    }
}
