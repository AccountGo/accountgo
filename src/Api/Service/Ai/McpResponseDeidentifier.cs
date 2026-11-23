// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;

namespace Api.Service.Ai;

public interface IMcpResponseDeidentifier
{
    ValueTask<CallToolResult> DeidentifyAsync(
        string? toolName,
        CallToolResult result,
        CancellationToken cancellationToken = default);

    Task<JsonNode?> DeidentifyJsonAsync(
        string? toolName,
        JsonNode? root,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Central fail-closed de-identification pipeline for every MCP tool result.
/// Registered as an MCP <c>CallToolFilter</c> so no tool can bypass de-identification.
/// </summary>
public sealed class McpResponseDeidentifier : IMcpResponseDeidentifier
{
    private readonly ApiDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IMcpCallerSessionContext _sessionContext;
    private readonly IAssistantPseudonymService _pseudonymService;

    private Dictionary<string, string>? _bankAccountPublicIdByLedgerAccountPublicId;

    public McpResponseDeidentifier(
        ApiDbContext db,
        ITenantContext tenantContext,
        IMcpCallerSessionContext sessionContext,
        IAssistantPseudonymService pseudonymService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _pseudonymService = pseudonymService ?? throw new ArgumentNullException(nameof(pseudonymService));
    }

    public async ValueTask<CallToolResult> DeidentifyAsync(
        string? toolName,
        CallToolResult result,
        CancellationToken cancellationToken = default)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        if (result.Content is not null)
        {
            for (var i = 0; i < result.Content.Count; i++)
            {
                if (result.Content[i] is TextContentBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
                {
                    var sanitized = await DeidentifyTextPayloadAsync(toolName, textBlock.Text, cancellationToken)
                        .ConfigureAwait(false);
                    result.Content[i] = new TextContentBlock
                    {
                        Text = sanitized,
                        Annotations = textBlock.Annotations,
                        Meta = textBlock.Meta,
                    };
                }
            }
        }

        if (result.StructuredContent is JsonElement structured)
        {
            var cloned = JsonNode.Parse(structured.GetRawText());
            var deidentified = await DeidentifyJsonAsync(toolName, cloned, cancellationToken)
                .ConfigureAwait(false);
            if (deidentified is not null)
            {
                using var doc = JsonDocument.Parse(deidentified.ToJsonString());
                result.StructuredContent = doc.RootElement.Clone();
            }
        }

        return result;
    }

    public async Task<JsonNode?> DeidentifyJsonAsync(
        string? toolName,
        JsonNode? root,
        CancellationToken cancellationToken = default)
    {
        if (root is null)
            return null;

        var bankMap = await GetBankAccountMapAsync(cancellationToken).ConfigureAwait(false);
        var rootKind = (root as JsonObject)?["kind"]?.GetValueKind() == JsonValueKind.String
            ? (root as JsonObject)!["kind"]!.GetValue<string>()
            : null;

        // Pass 1: collect all pseudonymization targets so we can upsert/resolve in one batch.
        var targets = new List<PseudonymTarget>();
        CollectTargets(root, parentProperty: null, toolName, rootKind, bankMap, targets);

        var tokenMap = await ResolveBatchTokensAsync(targets, cancellationToken).ConfigureAwait(false);

        // Pass 2: rewrite JSON tree in-place (fail-closed).
        TransformNode(root, parentProperty: null, toolName, rootKind, bankMap, tokenMap);
        return root;
    }

    private async Task<string> DeidentifyTextPayloadAsync(
        string? toolName,
        string rawText,
        CancellationToken cancellationToken)
    {
        var trimmed = rawText.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                var node = JsonNode.Parse(rawText);
                if (node is not null)
                {
                    await DeidentifyJsonAsync(toolName, node, cancellationToken).ConfigureAwait(false);
                    return node.ToJsonString();
                }
            }
            catch (JsonException)
            {
                // Fall through to fail-closed text masking.
            }
        }

        // Non-JSON text payload: fail closed by scrubbing embedded PII.
        return McpFieldClassificationRegistry.ScrubEmbeddedPii(rawText);
    }

    private async Task<IReadOnlyDictionary<AssistantPseudonymEntity, string>> ResolveBatchTokensAsync(
        List<PseudonymTarget> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
            return new Dictionary<AssistantPseudonymEntity, string>();

        var companyId = _tenantContext.CompanyId ?? 0;
        var userId = _tenantContext.UserId;

        if (companyId <= 0 || string.IsNullOrWhiteSpace(userId))
        {
            // No resolved tenant/user (e.g. unauthenticated or anonymous refusal) -> fail closed with ephemeral tokens.
            var fallback = new Dictionary<AssistantPseudonymEntity, string>();
            foreach (var t in targets)
            {
                if (!fallback.ContainsKey(t.Entity))
                {
                    var prefix = AssistantPseudonymKind.RolePrefix(t.Entity.Kind);
                    fallback[t.Entity] = $"{prefix}-{ComputeShortHash($"{t.Entity.Kind}|{t.Entity.PublicId}")}";
                }
            }
            return fallback;
        }

        return await _pseudonymService.PseudonymizeBatchAsync(
                companyId,
                userId,
                _sessionContext.SessionId,
                targets.Select(t => t.Entity),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> GetBankAccountMapAsync(CancellationToken cancellationToken)
    {
        if (_bankAccountPublicIdByLedgerAccountPublicId is not null)
            return _bankAccountPublicIdByLedgerAccountPublicId;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_tenantContext.IsResolved && _tenantContext.CompanyId is > 0)
        {
            var pairs = await (
                from bank in _db.Banks.AsNoTracking()
                join account in _db.Accounts.AsNoTracking() on bank.AccountId equals account.Id
                where !string.IsNullOrEmpty(bank.PublicId) && !string.IsNullOrEmpty(account.PublicId)
                select new { AccountPublicId = account.PublicId, BankPublicId = bank.PublicId }
            ).ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var p in pairs)
            {
                map[p.AccountPublicId] = p.BankPublicId;
            }
        }

        _bankAccountPublicIdByLedgerAccountPublicId = map;
        return map;
    }

    private static void CollectTargets(
        JsonNode node,
        string? parentProperty,
        string? toolName,
        string? rootKind,
        Dictionary<string, string> bankMap,
        List<PseudonymTarget> targets)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                {
                    if (kvp.Value is null)
                        continue;

                    if (kvp.Value is JsonValue val && val.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str))
                    {
                        var entity = TryResolvePseudonymEntity(obj, kvp.Key, str, toolName, rootKind, bankMap);
                        if (entity is not null)
                        {
                            targets.Add(new PseudonymTarget(entity));
                        }
                    }
                    else
                    {
                        CollectTargets(kvp.Value, kvp.Key, toolName, rootKind, bankMap, targets);
                    }
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not null)
                    {
                        CollectTargets(item, parentProperty, toolName, rootKind, bankMap, targets);
                    }
                }
                break;
        }
    }

    private static void TransformNode(
        JsonNode node,
        string? parentProperty,
        string? toolName,
        string? rootKind,
        Dictionary<string, string> bankMap,
        IReadOnlyDictionary<AssistantPseudonymEntity, string> tokenMap)
    {
        switch (node)
        {
            case JsonObject obj:
                var keys = obj.Select(p => p.Key).ToList();
                foreach (var key in keys)
                {
                    var child = obj[key];
                    if (child is null)
                        continue;

                    if (child is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        var replaced = TransformStringProperty(obj, key, str, toolName, rootKind, bankMap, tokenMap);
                        if (!string.Equals(replaced, str, StringComparison.Ordinal))
                        {
                            obj[key] = JsonValue.Create(replaced);
                        }
                    }
                    else if (child is JsonArray arr)
                    {
                        TransformArray(arr, key, toolName, rootKind, bankMap, tokenMap);
                    }
                    else
                    {
                        TransformNode(child, key, toolName, rootKind, bankMap, tokenMap);
                    }
                }
                break;

            case JsonArray arr:
                TransformArray(arr, parentProperty, toolName, rootKind, bankMap, tokenMap);
                break;
        }
    }

    private static void TransformArray(
        JsonArray arr,
        string? arrayPropertyName,
        string? toolName,
        string? rootKind,
        Dictionary<string, string> bankMap,
        IReadOnlyDictionary<AssistantPseudonymEntity, string> tokenMap)
    {
        var disposition = arrayPropertyName is not null
            ? McpFieldClassificationRegistry.Classify(arrayPropertyName)
            : McpFieldDisposition.MaskSensitive;

        for (var i = 0; i < arr.Count; i++)
        {
            var item = arr[i];
            if (item is null)
                continue;

            if (item is JsonValue val && val.TryGetValue<string>(out var str))
            {
                if (string.IsNullOrEmpty(str))
                    continue;

                var replaced = disposition switch
                {
                    McpFieldDisposition.KeepUnmasked => str,
                    McpFieldDisposition.KeepAndScrub => McpFieldClassificationRegistry.ScrubEmbeddedPii(str),
                    McpFieldDisposition.HideFreeText => McpFieldClassificationRegistry.HiddenTextPlaceholder,
                    _ => McpFieldClassificationRegistry.MaskedPlaceholder,
                };
                if (!string.Equals(replaced, str, StringComparison.Ordinal))
                {
                    arr[i] = JsonValue.Create(replaced);
                }
            }
            else
            {
                TransformNode(item, arrayPropertyName, toolName, rootKind, bankMap, tokenMap);
            }
        }
    }

    private static string TransformStringProperty(
        JsonObject owner,
        string propertyName,
        string value,
        string? toolName,
        string? rootKind,
        Dictionary<string, string> bankMap,
        IReadOnlyDictionary<AssistantPseudonymEntity, string> tokenMap)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var entity = TryResolvePseudonymEntity(owner, propertyName, value, toolName, rootKind, bankMap);
        if (entity is not null && tokenMap.TryGetValue(entity, out var token))
        {
            return token;
        }

        var disposition = ResolveEffectiveDisposition(owner, propertyName, toolName, rootKind, bankMap);
        return disposition switch
        {
            McpFieldDisposition.KeepUnmasked => value,
            McpFieldDisposition.KeepAndScrub => McpFieldClassificationRegistry.ScrubEmbeddedPii(value),
            McpFieldDisposition.HideFreeText => McpFieldClassificationRegistry.HiddenTextPlaceholder,
            McpFieldDisposition.MaskSensitive => McpFieldClassificationRegistry.MaskedPlaceholder,
            _ => McpFieldClassificationRegistry.MaskedPlaceholder,
        };
    }

    private static McpFieldDisposition ResolveEffectiveDisposition(
        JsonObject owner,
        string propertyName,
        string? toolName,
        string? rootKind,
        Dictionary<string, string> bankMap)
    {
        var baseDisposition = McpFieldClassificationRegistry.Classify(propertyName);
        if (baseDisposition == McpFieldDisposition.AccountNameOrBankPseudonym)
        {
            var acctId = GetStringProp(owner, "accountId")
                ?? GetStringProp(owner, "ledgerAccountId")
                ?? GetStringProp(owner, "linkedAccountId")
                ?? GetStringProp(owner, "id");

            if (!string.IsNullOrEmpty(acctId) && bankMap.ContainsKey(acctId))
                return McpFieldDisposition.PseudonymizeBankAccount;

            // If this object itself has a bankAccountId / bankId (e.g. bill payment AccountName), treat as bank pseudonym if bank-linked.
            if (GetBoolProp(owner, "isCash") == true && !string.IsNullOrEmpty(acctId) && bankMap.ContainsKey(acctId))
                return McpFieldDisposition.PseudonymizeBankAccount;

            return McpFieldDisposition.KeepAndScrub;
        }

        if (baseDisposition == McpFieldDisposition.ContextualName)
        {
            var id = GetStringProp(owner, "id") ?? GetStringProp(owner, "publicId");
            if (!string.IsNullOrEmpty(id))
            {
                if (id.StartsWith("cus_", StringComparison.OrdinalIgnoreCase)) return McpFieldDisposition.PseudonymizeCustomer;
                if (id.StartsWith("ven_", StringComparison.OrdinalIgnoreCase)) return McpFieldDisposition.PseudonymizeVendor;
                if (id.StartsWith("ba_", StringComparison.OrdinalIgnoreCase)) return McpFieldDisposition.PseudonymizeBankAccount;
                if (id.StartsWith("mem_", StringComparison.OrdinalIgnoreCase)) return McpFieldDisposition.PseudonymizePerson;
                if (id.StartsWith("acct_", StringComparison.OrdinalIgnoreCase))
                    return bankMap.ContainsKey(id) ? McpFieldDisposition.PseudonymizeBankAccount : McpFieldDisposition.KeepAndScrub;
                if (id.StartsWith("item_", StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith("tax_", StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith("fy_", StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith("cmp_", StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith("org_", StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith("role_", StringComparison.OrdinalIgnoreCase))
                    return McpFieldDisposition.KeepAndScrub;
            }

            // Fallback to tool/kind/shape context for objects without a prefixed `id`.
            if (owner.ContainsKey("accountClass") || owner.ContainsKey("normalBalance") || owner.ContainsKey("isContraAccount"))
                return McpFieldDisposition.KeepAndScrub;
            if (owner.ContainsKey("rate") || owner.ContainsKey("taxRate") || owner.ContainsKey("sku") || owner.ContainsKey("unitOfMeasure"))
                return McpFieldDisposition.KeepAndScrub;
            if (owner.ContainsKey("startDate") && owner.ContainsKey("endDate") && (rootKind == "fiscalYears" || toolName == "get_fiscal_years"))
                return McpFieldDisposition.KeepAndScrub;
            if (owner.ContainsKey("slug") && owner.ContainsKey("currency"))
                return McpFieldDisposition.KeepAndScrub;
            if (owner.ContainsKey("role") || owner.ContainsKey("email") || owner.ContainsKey("phone") || owner.ContainsKey("userId"))
                return McpFieldDisposition.PseudonymizePerson;

            if (string.Equals(rootKind, "clients", StringComparison.OrdinalIgnoreCase) || string.Equals(rootKind, "client", StringComparison.OrdinalIgnoreCase))
                return McpFieldDisposition.PseudonymizeCustomer;
            if (string.Equals(rootKind, "vendors", StringComparison.OrdinalIgnoreCase) || string.Equals(rootKind, "vendor", StringComparison.OrdinalIgnoreCase))
                return McpFieldDisposition.PseudonymizeVendor;
            if (string.Equals(rootKind, "bankAccounts", StringComparison.OrdinalIgnoreCase) || string.Equals(rootKind, "bankAccount", StringComparison.OrdinalIgnoreCase))
                return McpFieldDisposition.PseudonymizeBankAccount;
            if (string.Equals(rootKind, "members", StringComparison.OrdinalIgnoreCase) || string.Equals(toolName, "get_members_and_roles", StringComparison.OrdinalIgnoreCase))
                return McpFieldDisposition.PseudonymizePerson;

            // Fail closed on unknown "name" context!
            return McpFieldDisposition.MaskSensitive;
        }

        if (baseDisposition == McpFieldDisposition.PseudonymizeContextualParty)
        {
            var partyId = GetStringProp(owner, "partyId")
                ?? GetStringProp(owner, "clientId")
                ?? GetStringProp(owner, "customerId")
                ?? GetStringProp(owner, "vendorId")
                ?? GetStringProp(owner, "supplierId")
                ?? GetStringProp(owner, "bankId")
                ?? GetStringProp(owner, "id");

            if (!string.IsNullOrEmpty(partyId))
            {
                if (partyId.StartsWith("cus_", StringComparison.OrdinalIgnoreCase)) return McpFieldDisposition.PseudonymizeCustomer;
                if (partyId.StartsWith("ven_", StringComparison.OrdinalIgnoreCase)) return McpFieldDisposition.PseudonymizeVendor;
                if (partyId.StartsWith("ba_", StringComparison.OrdinalIgnoreCase)) return McpFieldDisposition.PseudonymizeBankAccount;
            }

            if (string.Equals(rootKind, "receivablesAging", StringComparison.OrdinalIgnoreCase))
                return McpFieldDisposition.PseudonymizeCustomer;
            if (string.Equals(rootKind, "payablesAging", StringComparison.OrdinalIgnoreCase))
                return McpFieldDisposition.PseudonymizeVendor;

            return McpFieldDisposition.PseudonymizePerson;
        }

        return baseDisposition;
    }

    private static AssistantPseudonymEntity? TryResolvePseudonymEntity(
        JsonObject owner,
        string propertyName,
        string value,
        string? toolName,
        string? rootKind,
        Dictionary<string, string> bankMap)
    {
        var effective = ResolveEffectiveDisposition(owner, propertyName, toolName, rootKind, bankMap);
        return effective switch
        {
            McpFieldDisposition.PseudonymizeCustomer => new AssistantPseudonymEntity(
                AssistantPseudonymKind.Customer,
                FindSiblingId(owner, "cus_", "clientId", "customerId", "partyId", "id")
                    ?? BuildSyntheticKey(AssistantPseudonymKind.Customer, value)),

            McpFieldDisposition.PseudonymizeVendor => new AssistantPseudonymEntity(
                AssistantPseudonymKind.Vendor,
                FindSiblingId(owner, "ven_", "vendorId", "supplierId", "partyId", "id")
                    ?? BuildSyntheticKey(AssistantPseudonymKind.Vendor, value)),

            McpFieldDisposition.PseudonymizeBankAccount => new AssistantPseudonymEntity(
                AssistantPseudonymKind.BankAccount,
                ResolveBankPublicId(owner, bankMap)
                    ?? BuildSyntheticKey(AssistantPseudonymKind.BankAccount, value)),

            McpFieldDisposition.PseudonymizePerson => new AssistantPseudonymEntity(
                AssistantPseudonymKind.Person,
                FindSiblingId(owner, "mem_", "memberId", "companyMemberId", "id")
                    ?? BuildSyntheticKey(AssistantPseudonymKind.Person, value)),

            _ => null,
        };
    }

    private static string? ResolveBankPublicId(JsonObject owner, Dictionary<string, string> bankMap)
    {
        var direct = FindSiblingId(owner, "ba_", "bankId", "bankAccountId", "id");
        if (direct is not null)
            return direct;

        foreach (var prop in new[] { "accountId", "ledgerAccountId", "linkedAccountId", "id" })
        {
            var acctId = GetStringProp(owner, prop);
            if (!string.IsNullOrEmpty(acctId) && bankMap.TryGetValue(acctId, out var bankPublicId))
            {
                return bankPublicId;
            }
        }

        return null;
    }

    private static string? FindSiblingId(JsonObject owner, string expectedPrefix, params string[] candidatePropertyNames)
    {
        foreach (var name in candidatePropertyNames)
        {
            var candidate = GetStringProp(owner, name);
            if (!string.IsNullOrEmpty(candidate) && candidate.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        return null;
    }

    private static string? GetStringProp(JsonObject obj, string name)
    {
        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase)
                && kvp.Value is JsonValue val
                && val.TryGetValue<string>(out var str)
                && !string.IsNullOrWhiteSpace(str))
            {
                return str.Trim();
            }
        }
        return null;
    }

    private static bool? GetBoolProp(JsonObject obj, string name)
    {
        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase)
                && kvp.Value is JsonValue val
                && val.TryGetValue<bool>(out var b))
            {
                return b;
            }
        }
        return null;
    }

    internal static string BuildSyntheticKey(string kind, string rawValue) =>
        $"raw:{kind}:{ComputeShortHash(rawValue.Trim().ToLowerInvariant())}";

    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    private sealed record PseudonymTarget(AssistantPseudonymEntity Entity);
}
