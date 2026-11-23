#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Api.Mcp.Models;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace Api.Mcp.Tools
{
    /// <summary>
    /// Shared plumbing for the workspace data tools: every read goes through <see cref="McpControllerReader"/>, so a
    /// tool returns what the matching page shows, under that page's access rules, for the member the run is for.
    /// </summary>
    public abstract class WorkspaceMcpToolBase
    {
        public const int DefaultTake = 25;
        public const int MaxTake = 100;

        /// <summary>Fields that hold full bank or card numbers. Masked numbers stay; full ones never reach the model.</summary>
        private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "accountNumber", "routingNumber", "iban", "swift", "swiftCode", "bic", "cardNumber"
        };

        protected WorkspaceMcpToolBase(McpControllerReader reader, ITenantContext tenant)
        {
            Reader = reader;
            Tenant = tenant;
        }

        protected McpControllerReader Reader { get; }

        /// <summary>A tool's id argument after translation: the integer key, or the response to return instead.</summary>
        protected readonly record struct IdArg(int? Id, WorkspaceDataMcpResponse? Failure);

        /// <summary>
        /// Translates a prefixed public id argument (for example <c>inv_…</c>) to the integer key under the member's
        /// workspace scope. A blank value means "not supplied" (<see cref="IdArg.Id"/> null, no failure). A wrong kind
        /// or malformed id is an invalid request; an unknown id and another workspace's id give the same not-found
        /// answer.
        /// </summary>
        protected async Task<IdArg> ResolveIdAsync(string kind, string? publicId, string prefix, string paramName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(publicId))
                return new IdArg(null, null);

            var candidate = publicId.Trim();
            if (Api.Service.Ai.AssistantPseudonymService.IsPseudonymToken(candidate))
            {
                var expectedKind = prefix switch
                {
                    "cus" => Api.Service.Ai.AssistantPseudonymKind.Customer,
                    "ven" => Api.Service.Ai.AssistantPseudonymKind.Vendor,
                    "ba" => Api.Service.Ai.AssistantPseudonymKind.BankAccount,
                    "mem" => Api.Service.Ai.AssistantPseudonymKind.Person,
                    _ => null,
                };

                var sessionId = Reader.CallerSession?.SessionId;
                if (expectedKind is null
                    || Tenant.CompanyId is not int companyId
                    || string.IsNullOrWhiteSpace(Tenant.UserId)
                    || string.IsNullOrWhiteSpace(sessionId)
                    || Reader.Pseudonyms is null)
                {
                    return new IdArg(null, Invalid(kind, $"{paramName} must be an id like '{prefix}_…', as returned by the tools that list these records."));
                }

                var mapped = await Reader.Pseudonyms.ResolveTokenAsync(companyId, Tenant.UserId, sessionId, candidate, cancellationToken);
                if (mapped is null || !string.Equals(mapped.Kind, expectedKind, StringComparison.Ordinal))
                {
                    return new IdArg(null, Invalid(kind, $"{paramName} must be an id like '{prefix}_…', as returned by the tools that list these records."));
                }

                candidate = mapped.PublicId;
            }

            var result = await Reader.Ids.ResolveAsync(candidate, prefix, cancellationToken);
            if (result.Ok)
                return new IdArg(result.Id, null);

            return result.Error == Api.PublicIds.PublicIdLookupError.NotFound
                ? new IdArg(null, NotFound(kind, $"No record '{candidate}' for {paramName} in this workspace."))
                : new IdArg(null, Invalid(kind, $"{paramName} must be an id like '{prefix}_…', as returned by the tools that list these records."));
        }

        /// <summary>The public id for a record, or null when there is none (id 0 or null).</summary>
        protected string? Pub(string prefix, int id) => id > 0 ? Reader.Ids.ToPublic(prefix, id) : null;

        /// <inheritdoc cref="Pub(string, int)"/>
        protected string? Pub(string prefix, int? id) => id is > 0 ? Reader.Ids.ToPublic(prefix, id.Value) : null;

        /// <summary>Loads the public ids of many records in one query per prefix, so building rows costs no extra lookups.</summary>
        protected Task PrefetchAsync(IEnumerable<(string Prefix, int Id)> ids, CancellationToken cancellationToken) =>
            Reader.Ids.PrefetchAsync(ids.Where(i => i.Id > 0), cancellationToken);

        protected ITenantContext Tenant { get; }

        protected string Organization => Tenant.CompanySlug ?? $"Tenant-{Tenant.CompanyId}";

        /// <summary>A path inside the current workspace, e.g. <c>/org/acme/sales/12</c>.</summary>
        protected string PagePath(string relative) => $"/org/{Tenant.CompanySlug}/{relative.TrimStart('/')}";

        /// <summary>Reads one GET action and returns its value as a record response.</summary>
        protected async Task<WorkspaceDataMcpResponse> RecordAsync<TController>(
            string kind,
            string actionName,
            Func<TController, object> invoke,
            string? link = null,
            IDictionary<string, object?>? arguments = null,
            Func<object?, object?>? shape = null)
            where TController : ControllerBase
        {
            var read = await Reader.ReadAsync(actionName, invoke, arguments);
            if (!read.Succeeded)
                return Refused(kind, read);

            return Success(kind, shape != null ? shape(read.Value) : read.Value, link: link);
        }

        protected WorkspaceDataMcpResponse Success(
            string kind,
            object? data,
            string? link = null,
            int? totalCount = null,
            int? skip = null,
            int? take = null) =>
            new()
            {
                Organization = Organization,
                Status = FinancialMcpStatus.Success,
                Kind = kind,
                Link = link,
                TotalCount = totalCount,
                Skip = skip,
                Take = take,
                Data = Redact(Reader.ToJson(data))
            };

        /// <summary>Pages an already-filtered sequence and returns it with its total.</summary>
        protected WorkspaceDataMcpResponse Page<T>(string kind, IEnumerable<T> items, int skip, int take, string? link = null)
        {
            var list = items as IList<T> ?? items.ToList();
            return Success(kind, list.Skip(skip).Take(take).ToList(), link, list.Count, skip, take);
        }

        /// <summary>
        /// Pages first, then loads the public ids of only the returned rows in one query per prefix, then projects them.
        /// Projecting every filtered row would look up ids one at a time for rows that are never returned.
        /// </summary>
        protected async Task<WorkspaceDataMcpResponse> PageAsync<T>(
            string kind,
            IReadOnlyList<T> all,
            int skip,
            int take,
            Func<T, IEnumerable<(string Prefix, int Id)>> idsOf,
            Func<T, object> project,
            string? link,
            CancellationToken cancellationToken)
        {
            var page = all.Skip(skip).Take(take).ToList();
            await PrefetchAsync(page.SelectMany(idsOf), cancellationToken);
            return Success(kind, page.Select(project).ToList(), link, all.Count, skip, take);
        }

        protected WorkspaceDataMcpResponse Refused(string kind, McpReadResult read) =>
            new()
            {
                Organization = Organization,
                Status = read.Status,
                Message = read.Message,
                RequiredModules = read.RequiredModules,
                Kind = kind
            };

        protected WorkspaceDataMcpResponse Invalid(string kind, string message) =>
            new()
            {
                Organization = Organization,
                Status = FinancialMcpStatus.InvalidRequest,
                Message = message,
                Kind = kind
            };

        protected WorkspaceDataMcpResponse NotFound(string kind, string message) =>
            new()
            {
                Organization = Organization,
                Status = FinancialMcpStatus.NotFound,
                Message = message,
                Kind = kind
            };

        protected static void ValidatePaging(int skip, int take)
        {
            if (skip < 0)
                throw new ArgumentException("Skip must be greater than or equal to 0.", nameof(skip));
            if (take < 1 || take > MaxTake)
                throw new ArgumentException($"Take must be between 1 and {MaxTake}.", nameof(take));
        }

        protected static DateTime? ParseDate(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.Date;
            throw new ArgumentException($"Invalid date for '{paramName}'. Use an ISO-8601 date such as '2026-01-31'.", paramName);
        }

        protected static bool Matches(string? text, params string?[] fields) =>
            string.IsNullOrWhiteSpace(text) ||
            fields.Any(f => !string.IsNullOrEmpty(f) && f.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase));

        protected static bool SameStatus(string? actual, string? wanted) =>
            string.IsNullOrWhiteSpace(wanted) ||
            string.Equals(Normalize(actual), Normalize(wanted), StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string? status) =>
            (status ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

        /// <summary>Removes full account numbers anywhere in the payload.</summary>
        internal static JsonElement Redact(JsonElement element)
        {
            if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return element;

            var node = JsonNode.Parse(element.GetRawText());
            if (node == null) return element;
            Strip(node);
            using var document = JsonDocument.Parse(node.ToJsonString());
            return document.RootElement.Clone();
        }

        private static void Strip(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).Where(SensitiveFields.Contains).ToList())
                        obj.Remove(key);
                    foreach (var child in obj.Select(p => p.Value).Where(v => v != null).ToList())
                        Strip(child!);
                    break;
                case JsonArray array:
                    foreach (var child in array.Where(v => v != null).ToList())
                        Strip(child!);
                    break;
            }
        }
    }
}
