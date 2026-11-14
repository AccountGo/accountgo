// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Banking
{
    public sealed record StatementOwnerActionDto(int AccountId, string AccountCode, string AccountName, string Label);
    public sealed record StatementPartnerOwnerActionsDto(int PartnerId, string Name, StatementOwnerActionDto? Drawing, StatementOwnerActionDto? Contribution);
    public sealed record StatementOwnerActionsDto(StatementOwnerActionDto? Drawing, StatementOwnerActionDto? Contribution, IReadOnlyList<StatementPartnerOwnerActionsDto>? Partners = null);

    public sealed class UndoOwnerMovementRequest
    {
        public string? Reason { get; set; }
    }

    public class RecordOwnerMovementRequest
    {
        public string? Memo { get; set; }
        public bool RememberRule { get; set; }
        public int? PartnerId { get; set; }
    }

    public sealed class BulkRecordOwnerMovementRequest : RecordOwnerMovementRequest
    {
        public IReadOnlyList<int> LineIds { get; set; } = Array.Empty<int>();
    }
}
