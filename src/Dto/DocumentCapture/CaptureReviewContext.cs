// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Dto.Common;

namespace Dto.DocumentCapture
{
    /// <summary>
    /// Narrowly scoped review context sent with a financial document save, approval, or issue when backed by an
    /// accepted document capture. Authoritative totals are always recalculated on the server from the persisted
    /// entity and canonical capture fields.
    /// </summary>
    public class CaptureReviewContext
    {
        [PublicId("cap")]
        public int CaptureId { get; set; }
        /// <summary>Canonical source grand total snapshot the reviewer saw.</summary>
        public decimal? SourceGrandTotal { get; set; }
        /// <summary>Authoritative document total snapshot the reviewer saw.</summary>
        public decimal? FormTotal { get; set; }
        /// <summary>Explicit acknowledgement of a discrepancy between source and form totals.</summary>
        public bool DiscrepancyAcknowledged { get; set; }
    }
}
