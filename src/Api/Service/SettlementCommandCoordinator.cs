// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Service
{
    public sealed class SettlementCommandCoordinator
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly Core.Authorization.IAccessAuthorizationService? _auth;

        public SettlementCommandCoordinator(
            ApiDbContext db,
            ITenantContext tenant,
            Core.Authorization.IAccessAuthorizationService? auth = null)
        {
            _db = db;
            _tenant = tenant;
            _auth = auth;
        }

        public void EnsureBankingPickerAccess()
        {
            if (_auth == null)
                return;
            var decision = _auth.AuthorizeRequirement(operationKey: Core.Authorization.AccessOperationCatalog.BankingAccountsPicker);
            if (!decision.Allowed)
                throw new SettlementCommandException(SettlementErrorCodes.Forbidden, decision.SafeMessage, decision.HttpStatus);
        }

        public CompanySetting EnsureFeatureEnabled(int companyId)
        {
            var setting = _db.CompanySettings.FirstOrDefault(s => s.CompanyId == companyId);
            if (setting == null || !setting.CreditsReturnsRefundsEnabled)
            {
                throw new SettlementCommandException(
                    SettlementErrorCodes.FeatureDisabled,
                    "Credits, returns, refunds, and write-offs are not enabled for this workspace.",
                    403);
            }

            _ = CurrencyPrecisionRules.RequireSupportedPrecision(setting.Currency);
            return setting;
        }

        public void EnsureReviewPolicyForPosting(int companyId, string? submittedBy, string? actorUserId, bool requiresTeamReview)
        {
            if (!requiresTeamReview)
                return;

            var settings = _db.WorkspaceOperatingSettings
                .AsNoTracking()
                .FirstOrDefault(s => s.CompanyId == companyId);

            // Solo workspaces follow their explicitly permitted, audited self-post path.
            var isSolo = settings != null &&
                (settings.CollaborationState == WorkspaceCollaborationState.SoloActive ||
                 settings.Purpose == WorkspacePurpose.Personal);

            if (isSolo)
                return;

            // Team or unclassified workspaces enforce non-self-approval for review-required postings.
            if (string.IsNullOrWhiteSpace(submittedBy))
            {
                throw new SettlementCommandException(
                    SettlementErrorCodes.SelfApprovalForbidden,
                    "Team operating policy requires submitting for independent review before posting.",
                    403);
            }

            if (!string.IsNullOrWhiteSpace(actorUserId) &&
                string.Equals(submittedBy.Trim(), actorUserId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new SettlementCommandException(
                    SettlementErrorCodes.SelfApprovalForbidden,
                    "Team operating policy prohibits self-approval. Another eligible reviewer must approve and post this item.",
                    403);
            }
        }

        public TResult Execute<TRequest, TResult>(
            string operationKey,
            string? idempotencyKey,
            TRequest request,
            Func<int, CompanySetting, TResult> action,
            Func<TResult, int> entityIdSelector,
            string resultEntityType)
        {
            var companyId = _tenant.RequireCompanyId();
            var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
            var payloadHash = ComputePayloadHash(request);

            using var tx = BeginSerializableTransaction();
            try
            {
                var setting = EnsureFeatureEnabled(companyId);

                if (normalizedKey != null)
                {
                    var existing = _db.SettlementCommandReceipts
                        .AsNoTracking()
                        .FirstOrDefault(r =>
                            r.CompanyId == companyId &&
                            r.OperationKey == operationKey &&
                            r.IdempotencyKey == normalizedKey);

                    if (existing != null)
                    {
                        if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                        {
                            throw new SettlementCommandException(
                                SettlementErrorCodes.IdempotencyConflict,
                                "Idempotency key was already used with a different request payload.",
                                409);
                        }

                        var cached = JsonSerializer.Deserialize<TResult>(existing.ResultJson);
                        if (cached != null)
                        {
                            tx.Rollback();
                            return cached;
                        }
                    }
                }

                var result = action(companyId, setting);

                if (normalizedKey != null)
                {
                    _db.SettlementCommandReceipts.Add(new SettlementCommandReceipt
                    {
                        CompanyId = companyId,
                        OperationKey = operationKey,
                        IdempotencyKey = normalizedKey,
                        PayloadHash = payloadHash,
                        ResultEntityType = resultEntityType,
                        ResultEntityId = entityIdSelector(result),
                        ResultJson = JsonSerializer.Serialize(result),
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                _db.SaveChanges();
                tx.Commit();
                return result;
            }
            catch (SettlementCommandException)
            {
                TryRollback(tx);
                throw;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                TryRollback(tx);
                throw new SettlementCommandException(
                    SettlementErrorCodes.StaleVersion,
                    "The document or balance changed concurrently. Refresh and try again.",
                    409,
                    ex);
            }
            catch (Exception ex) when (IsSerializationOrLockConflict(ex))
            {
                TryRollback(tx);
                throw new SettlementCommandException(
                    SettlementErrorCodes.StaleVersion,
                    "Concurrent financial update conflict. Refresh and retry with the same idempotency key.",
                    409,
                    ex);
            }
            catch (InvalidOperationException ex)
            {
                TryRollback(tx);
                var code = ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
                           ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)
                    ? SettlementErrorCodes.PeriodLocked
                    : ex.Message.Contains("reconcil", StringComparison.OrdinalIgnoreCase)
                        ? SettlementErrorCodes.ReconciliationProtection
                        : SettlementErrorCodes.InvalidState;
                var status = code is SettlementErrorCodes.PeriodLocked or SettlementErrorCodes.ReconciliationProtection ? 409 : 400;
                throw new SettlementCommandException(code, ex.Message, status, ex);
            }
            catch
            {
                TryRollback(tx);
                throw;
            }
        }

        private IDbContextTransaction BeginSerializableTransaction()
        {
            return _db.Database.BeginTransaction(IsolationLevel.Serializable);
        }

        private static void TryRollback(IDbContextTransaction tx)
        {
            try
            {
                tx.Rollback();
            }
            catch
            {
                // Ignore rollback failure on already-aborted transaction
            }
        }

        private static bool IsSerializationOrLockConflict(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                var msg = cur.Message ?? "";
                if (msg.Contains("40001", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("could not serialize access", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("SQLITE_BUSY", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static string ComputePayloadHash<T>(T payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes);
        }
    }
}
