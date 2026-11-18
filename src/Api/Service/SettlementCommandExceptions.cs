// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Api.Service
{
    public static class SettlementErrorCodes
    {
        public const string FeatureDisabled = "feature_disabled";
        public const string StaleVersion = "stale_version";
        public const string IdempotencyConflict = "idempotency_conflict";
        public const string SourceExhausted = "source_exhausted";
        public const string InvalidState = "invalid_state";
        public const string InvalidDate = "invalid_date";
        public const string InvalidCurrency = "invalid_currency";
        public const string MissingMapping = "missing_mapping";
        public const string MissingEvidence = "missing_evidence";
        public const string PeriodLocked = "period_locked";
        public const string ReconciliationProtection = "reconciliation_protection";
        public const string SelfApprovalForbidden = "self_approval_forbidden";
        public const string PermissionDenied = "permission_denied";
        public const string Forbidden = PermissionDenied;
        public const string NotFound = "not_found";
        public const string Validation = "validation_error";
    }

    public sealed class SettlementCommandException : Exception
    {
        public string Code { get; }
        public int HttpStatus { get; }

        public SettlementCommandException(string code, string message, int httpStatus = 400, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            HttpStatus = httpStatus;
        }
    }
}
