// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Dto.Banking;

namespace Services.Banking
{
    public class BankStatementImportService : IBankStatementImportService
    {
        /// <summary>Statement date and ledger date may differ by this many days and still match.</summary>
        public const int MatchWindowDays = 3;
        public const int PreviewRowLimit = 50;
        public const int DefaultListPageSize = 100;
        public const int MaxListPageSize = 500;
        public const string StatusUnmatched = "unmatched";
        public const string StatusMatched = "matched";
        public const string StatusExcluded = "excluded";
        public const string StatusAll = "all";
        public const string AlreadyImportedMessage = "Those statement lines were already imported.";
        private const string StatementLineUniqueIndexName = "UX_BankStatementLine_Company_Bank_Source_External";

        private readonly IRepository<BankStatementImport> _imports;
        private readonly IRepository<BankStatementLine> _lines;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly IRepository<GeneralLedgerHeader> _glHeaders;
        private readonly IBankTransactionService _transactions;
        private readonly ITenantContext _tenant;

        public BankStatementImportService(
            IRepository<BankStatementImport> imports,
            IRepository<BankStatementLine> lines,
            IRepository<Bank> banks,
            IRepository<GeneralLedgerLine> glLines,
            IRepository<GeneralLedgerHeader> glHeaders,
            IBankTransactionService transactions,
            ITenantContext tenant)
        {
            _imports = imports;
            _lines = lines;
            _banks = banks;
            _glLines = glLines;
            _glHeaders = glHeaders;
            _transactions = transactions;
            _tenant = tenant;
        }

        public BankStatementPreviewDto Preview(int bankId, BankStatementImportRequest request)
        {
            EnsureCompany();
            var bank = RequireActiveBank(bankId);
            var source = ParseSource(request);
            var rows = BankStatementParser.Parse(source, request.Content, request.ColumnMap);
            var existing = ExistingExternalIds(bank.Id, source);
            var classified = Classify(rows, existing);

            return new BankStatementPreviewDto
            {
                Source = (int)source,
                TotalRows = rows.Count,
                ReadyRows = classified.Count(c => c.Status == RowStatus.Ready),
                DuplicateRows = classified.Count(c => c.Status == RowStatus.Duplicate),
                ErrorRows = classified.Count(c => c.Status == RowStatus.Error),
                Rows = classified.Take(PreviewRowLimit).Select(ToPreviewRow).ToList()
            };
        }

        public BankStatementCommitResultDto Commit(int bankId, BankStatementImportRequest request, string actor)
        {
            EnsureCompany();
            var bank = RequireActiveBank(bankId);
            var source = ParseSource(request);
            var rows = BankStatementParser.Parse(source, request.Content, request.ColumnMap);
            var existing = ExistingExternalIds(bank.Id, source);
            var classified = Classify(rows, existing);

            var result = new BankStatementCommitResultDto
            {
                Duplicates = classified.Count(c => c.Status == RowStatus.Duplicate),
                Errors = classified.Count(c => c.Status == RowStatus.Error)
            };

            var ready = classified.Where(c => c.Status == RowStatus.Ready).Select(c => c.Row).ToList();
            if (ready.Count == 0)
                return result;

            var companyId = _tenant.RequireCompanyId();
            var now = DateTime.UtcNow;
            var import = new BankStatementImport
            {
                CompanyId = companyId,
                BankId = bank.Id,
                Source = source,
                FileName = TruncateOrNull(request.FileName, 260),
                ImportedAt = now,
                ImportedBy = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(),
                ColumnMapJson = source == BankStatementSources.Csv && request.ColumnMap != null
                    ? JsonSerializer.Serialize(request.ColumnMap)
                    : null
            };
            _imports.Insert(import);

            var newLines = ready.Select(row => new BankStatementLine
            {
                CompanyId = companyId,
                BankId = bank.Id,
                BankStatementImportId = import.Id,
                Source = source,
                ExternalId = row.ExternalId,
                PostedDate = row.PostedDate!.Value.Date,
                Amount = row.Amount!.Value,
                Payee = row.Payee,
                Memo = row.Memo,
                Reference = row.Reference,
                CreatedAt = now
            }).ToList();

            try
            {
                _lines.Insert(newLines);
            }
            catch (Exception ex) when (IsDuplicateStatementLine(ex))
            {
                // A concurrent import of the same file won the unique (bank, source, external id) index.
                throw new InvalidOperationException(AlreadyImportedMessage, ex);
            }

            result.ImportId = import.Id;
            result.Imported = ready.Count;
            result.AutoMatched = ApplyAutoMatches(bank, newLines);
            return result;
        }

        public BankStatementLineListDto ListLines(int bankId, string? status, int page = 1, int pageSize = DefaultListPageSize)
        {
            EnsureCompany();
            var bank = RequireBank(bankId);
            var normalized = NormalizeStatus(status);
            var companyId = _tenant.RequireCompanyId();

            if (page < 1)
                page = 1;
            if (pageSize < 1)
                pageSize = DefaultListPageSize;
            if (pageSize > MaxListPageSize)
                pageSize = MaxListPageSize;

            // Counts and the page slice stay on the query; only the current page (and the unmatched
            // set needed for candidate uniqueness) are materialised.
            var bankLines = _lines.Table.Where(l => l.CompanyId == companyId && l.BankId == bank.Id);
            var excludedCount = bankLines.Count(l => l.ExcludedAt != null);
            var unmatchedCount = bankLines.Count(l => l.ExcludedAt == null && l.MatchedGeneralLedgerLineId == null);
            var matchedCount = bankLines.Count(l => l.ExcludedAt == null && l.MatchedGeneralLedgerLineId != null);

            IQueryable<BankStatementLine> filtered = normalized switch
            {
                StatusMatched => bankLines.Where(l => l.ExcludedAt == null && l.MatchedGeneralLedgerLineId != null),
                StatusExcluded => bankLines.Where(l => l.ExcludedAt != null),
                StatusAll => bankLines,
                _ => bankLines.Where(l => l.ExcludedAt == null && l.MatchedGeneralLedgerLineId == null)
            };
            var totalCount = filtered.Count();
            var pageRows = filtered
                .OrderBy(l => l.PostedDate).ThenBy(l => l.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var unmatched = unmatchedCount == 0
                ? new List<BankStatementLine>()
                : bankLines.Where(l => l.ExcludedAt == null && l.MatchedGeneralLedgerLineId == null).ToList();
            var candidatePool = unmatched.Count == 0
                ? Array.Empty<PoolEntry>()
                : LoadCandidatePool(bank, unmatched);
            return new BankStatementLineListDto
            {
                UnmatchedCount = unmatchedCount,
                MatchedCount = matchedCount,
                ExcludedCount = excludedCount,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Lines = pageRows.Select(l => ToDto(l, l.MatchedGeneralLedgerLineId.HasValue || l.ExcludedAt.HasValue
                    ? Array.Empty<PoolEntry>()
                    : CandidatesFor(l, candidatePool))).ToList()
            };
        }

        public BankStatementLineDto Match(int lineId, MatchBankStatementLineRequest request)
        {
            EnsureCompany();
            var line = RequireLine(lineId);
            var bank = RequireActiveBank(line.BankId);
            EnsureNotExcluded(line);
            if (line.MatchedGeneralLedgerLineId.HasValue)
                throw new InvalidOperationException("That statement line is already matched. Unmatch it first.");

            if (request.GeneralLedgerLineId.HasValue)
            {
                var glLine = RequireMatchableLedgerLine(bank, line, request.GeneralLedgerLineId.Value);
                line.MatchedGeneralLedgerLineId = glLine.Id;
            }
            else
            {
                var unmatched = UnmatchedLines(bank);
                var plan = ComputeAutoMatches(bank, unmatched);
                if (!plan.TryGetValue(line.Id, out var glLineId))
                    throw new InvalidOperationException(
                        "No single ledger line matches this statement line. Pick one to match manually.");
                line.MatchedGeneralLedgerLineId = glLineId;
            }

            _lines.Update(line);
            return ToDto(line, Array.Empty<PoolEntry>());
        }

        public BankStatementLineDto Unmatch(int lineId)
        {
            EnsureCompany();
            var line = RequireLine(lineId);
            if (!line.MatchedGeneralLedgerLineId.HasValue)
                throw new InvalidOperationException("That statement line is not matched.");

            line.MatchedGeneralLedgerLineId = null;
            _lines.Update(line);
            return ToDto(line, Array.Empty<PoolEntry>());
        }

        public BankStatementLineDto Exclude(int lineId, ExcludeBankStatementLineRequest request, string actor)
        {
            EnsureCompany();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var line = RequireLine(lineId);
            if (line.MatchedGeneralLedgerLineId.HasValue)
                throw new InvalidOperationException("Unmatch the line before excluding it.");
            if (line.ExcludedAt.HasValue)
                throw new InvalidOperationException("That statement line is already excluded.");

            if (!Enum.IsDefined(typeof(BankStatementExclusionReasons), request.Reason))
                throw new InvalidOperationException("Pick a reason: Personal, Transfer between my accounts, Duplicate, or Other.");

            var reason = (BankStatementExclusionReasons)request.Reason;
            var note = TruncateOrNull(request.Note, 500);
            if (reason == BankStatementExclusionReasons.Other && string.IsNullOrWhiteSpace(note))
                throw new InvalidOperationException("Add a short note when the reason is Other.");

            line.ExcludedAt = DateTime.UtcNow;
            line.ExcludedBy = TruncateOrNull(actor, 100);
            line.ExclusionReason = reason;
            line.ExclusionNote = note;
            _lines.Update(line);
            return ToDto(line, Array.Empty<PoolEntry>());
        }

        public BankStatementLineDto Include(int lineId)
        {
            EnsureCompany();
            var line = RequireLine(lineId);
            if (!line.ExcludedAt.HasValue)
                throw new InvalidOperationException("That statement line is not excluded.");

            line.ExcludedAt = null;
            line.ExcludedBy = null;
            line.ExclusionReason = null;
            line.ExclusionNote = null;
            _lines.Update(line);
            return ToDto(line, Array.Empty<PoolEntry>());
        }

        public BankStatementLineDto CreateTransaction(int lineId, CreateTransactionFromStatementLineRequest request)
        {
            EnsureCompany();
            var line = RequireLine(lineId);
            var bank = RequireActiveBank(line.BankId);
            EnsureNotExcluded(line);
            if (line.MatchedGeneralLedgerLineId.HasValue)
                throw new InvalidOperationException("That statement line is already matched.");
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            var kind = request.Kind;
            var isDeposit = line.Amount > 0m;
            var allowedForDeposit = new[]
            {
                (int)BankTransactionKinds.MoneyIn, (int)BankTransactionKinds.Interest, (int)BankTransactionKinds.Adjustment
            };
            var allowedForPayment = new[]
            {
                (int)BankTransactionKinds.MoneyOut, (int)BankTransactionKinds.Fee, (int)BankTransactionKinds.Adjustment
            };
            if (!(isDeposit ? allowedForDeposit : allowedForPayment).Contains(kind))
                throw new InvalidOperationException(isDeposit
                    ? "A deposit can be recorded as Money in, Interest, or Adjustment."
                    : "A payment can be recorded as Money out, Fee, or Adjustment.");

            var memo = FirstNonBlank(request.Memo, line.Memo, line.Payee);
            var created = _transactions.Create(new CreateBankTransactionRequest
            {
                Kind = kind,
                BankId = bank.Id,
                Date = line.PostedDate.Date,
                Amount = Math.Abs(line.Amount),
                ContraAccountId = request.ContraAccountId,
                IncreaseBalance = kind == (int)BankTransactionKinds.Adjustment ? isDeposit : null,
                Memo = memo,
                ReferenceNo = line.Reference
            });

            if (!created.GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("The transaction did not post a ledger entry.");

            var bankAccountId = bank.AccountId.Value;
            var glLine = _glLines.Table.FirstOrDefault(l =>
                l.GeneralLedgerHeaderId == created.GeneralLedgerHeaderId.Value && l.AccountId == bankAccountId)
                ?? throw new InvalidOperationException("Could not find the bank line for the new transaction.");

            line.MatchedGeneralLedgerLineId = glLine.Id;
            _lines.Update(line);
            return ToDto(line, Array.Empty<PoolEntry>());
        }

        // ------------------------------------------------------------ matching

        private sealed record PoolEntry(
            int GeneralLedgerLineId,
            int HeaderId,
            DateTime Date,
            string? Description,
            decimal SignedAmount);

        /// <summary>
        /// Auto-match every unmatched line on the bank (so an earlier import can still pick up a ledger
        /// entry that arrived later), but return only how many lines from <paramref name="importedLines"/>
        /// were matched — the commit result must not credit this import for older rows.
        /// </summary>
        private int ApplyAutoMatches(Bank bank, IReadOnlyList<BankStatementLine> importedLines)
        {
            var unmatched = UnmatchedLines(bank);
            if (unmatched.Count == 0)
                return 0;

            var plan = ComputeAutoMatches(bank, unmatched);
            BankStatementLine? lastMatched = null;
            foreach (var line in unmatched)
            {
                if (!plan.TryGetValue(line.Id, out var glLineId))
                    continue;
                line.MatchedGeneralLedgerLineId = glLineId;
                lastMatched = line;
            }
            // The repository saves every tracked change, so one Update persists the whole plan.
            if (lastMatched != null)
                _lines.Update(lastMatched);

            var importedIds = new HashSet<int>(importedLines.Select(l => l.Id));
            return plan.Keys.Count(id => importedIds.Contains(id));
        }

        /// <summary>
        /// One-to-one plan: a statement line matches only when it has exactly one candidate ledger line and
        /// that ledger line is the candidate of no other statement line. Ambiguity leaves both unmatched.
        /// </summary>
        private Dictionary<int, int> ComputeAutoMatches(Bank bank, IReadOnlyList<BankStatementLine> unmatched)
        {
            var plan = new Dictionary<int, int>();
            if (unmatched.Count == 0)
                return plan;

            var pool = LoadCandidatePool(bank, unmatched);
            var candidatesByLine = unmatched.ToDictionary(l => l.Id, l => CandidatesFor(l, pool));

            var claimCount = new Dictionary<int, int>();
            foreach (var candidates in candidatesByLine.Values)
            {
                foreach (var candidate in candidates)
                {
                    claimCount.TryGetValue(candidate.GeneralLedgerLineId, out var count);
                    claimCount[candidate.GeneralLedgerLineId] = count + 1;
                }
            }

            foreach (var line in unmatched)
            {
                var candidates = candidatesByLine[line.Id];
                if (candidates.Count != 1)
                    continue;
                var only = candidates[0];
                if (claimCount[only.GeneralLedgerLineId] != 1)
                    continue;
                plan[line.Id] = only.GeneralLedgerLineId;
            }
            return plan;
        }

        private static IReadOnlyList<PoolEntry> CandidatesFor(BankStatementLine line, IReadOnlyList<PoolEntry> pool)
        {
            var postedDate = line.PostedDate.Date;
            return pool
                .Where(p => p.SignedAmount == line.Amount
                            && Math.Abs((p.Date.Date - postedDate).TotalDays) <= MatchWindowDays)
                .OrderBy(p => Math.Abs((p.Date.Date - postedDate).TotalDays))
                .ThenBy(p => p.GeneralLedgerLineId)
                .ToList();
        }

        /// <summary>Uncleared ledger lines on the bank account that no statement line has claimed yet.</summary>
        private IReadOnlyList<PoolEntry> LoadCandidatePool(Bank bank, IReadOnlyList<BankStatementLine> unmatched)
        {
            if (!bank.AccountId.HasValue || unmatched.Count == 0)
                return Array.Empty<PoolEntry>();

            var accountId = bank.AccountId.Value;
            var companyId = _tenant.RequireCompanyId();
            var windowStart = unmatched.Min(l => l.PostedDate.Date).AddDays(-MatchWindowDays);
            var windowEnd = unmatched.Max(l => l.PostedDate.Date).AddDays(MatchWindowDays);

            var claimed = _lines.Table
                .Where(l => l.CompanyId == companyId && l.BankId == bank.Id && l.MatchedGeneralLedgerLineId != null)
                .Select(l => l.MatchedGeneralLedgerLineId!.Value)
                .ToHashSet();

            var rows = (
                from line in _glLines.Table
                join header in _glHeaders.Table on line.GeneralLedgerHeaderId equals header.Id
                where line.AccountId == accountId
                      && line.BankReconciliationId == null
                      && header.Date >= windowStart && header.Date < windowEnd.AddDays(1)
                select new
                {
                    LineId = line.Id,
                    HeaderId = header.Id,
                    header.Date,
                    header.Description,
                    line.DrCr,
                    line.Amount,
                    header.ReversedGeneralLedgerHeaderId
                }).ToList()
                .Where(r => !claimed.Contains(r.LineId) && r.Date.Date >= windowStart && r.Date.Date <= windowEnd)
                .ToList();

            // A voided entry and its reversal never appear on a statement. The reversal can be dated outside the
            // window (or in another period), so a header counts as voided when any entry reverses it, wherever dated.
            var headerIds = rows.Select(r => r.HeaderId).Distinct().ToList();
            var voidPairHeaders = new HashSet<int>(VoidedHeaderIds(headerIds));
            foreach (var row in rows)
            {
                if (row.ReversedGeneralLedgerHeaderId.HasValue)
                    voidPairHeaders.Add(row.HeaderId);
            }

            return rows
                .Where(r => !voidPairHeaders.Contains(r.HeaderId))
                .Select(r => new PoolEntry(
                    r.LineId,
                    r.HeaderId,
                    r.Date.Date,
                    r.Description,
                    r.DrCr == DrOrCrSide.Dr ? r.Amount : -r.Amount))
                .ToList();
        }

        /// <summary>Headers among <paramref name="headerIds"/> that some reversing entry points at, whatever its date.</summary>
        private HashSet<int> VoidedHeaderIds(IReadOnlyCollection<int> headerIds)
        {
            if (headerIds.Count == 0)
                return new HashSet<int>();

            var companyId = _tenant.RequireCompanyId();
            return _glHeaders.Table
                .Where(h => h.CompanyId == companyId
                            && h.ReversedGeneralLedgerHeaderId != null
                            && headerIds.Contains(h.ReversedGeneralLedgerHeaderId.Value))
                .Select(h => h.ReversedGeneralLedgerHeaderId!.Value)
                .ToHashSet();
        }

        private GeneralLedgerLine RequireMatchableLedgerLine(Bank bank, BankStatementLine statementLine, int glLineId)
        {
            var glLine = _glLines.GetById(glLineId);
            if (glLine == null || glLine.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(GeneralLedgerLine), glLineId);
            if (!bank.AccountId.HasValue || glLine.AccountId != bank.AccountId.Value)
                throw new InvalidOperationException("That ledger line is not on this bank account.");
            if (glLine.BankReconciliationId.HasValue)
                throw new InvalidOperationException("That ledger line is already cleared by a reconciliation.");

            var header = _glHeaders.GetById(glLine.GeneralLedgerHeaderId);
            var isReversalEntry = header != null && header.ReversedGeneralLedgerHeaderId.HasValue;
            if (isReversalEntry || VoidedHeaderIds(new[] { glLine.GeneralLedgerHeaderId }).Count > 0)
                throw new InvalidOperationException("That ledger line belongs to a voided entry.");

            var signed = glLine.DrCr == DrOrCrSide.Dr ? glLine.Amount : -glLine.Amount;
            if (signed != statementLine.Amount)
                throw new InvalidOperationException("The ledger line amount must equal the statement line amount.");

            var companyId = _tenant.RequireCompanyId();
            if (_lines.Table.Any(l => l.CompanyId == companyId && l.MatchedGeneralLedgerLineId == glLine.Id))
                throw new InvalidOperationException("That ledger line is already matched to another statement line.");
            return glLine;
        }

        private List<BankStatementLine> UnmatchedLines(Bank bank)
        {
            var companyId = _tenant.RequireCompanyId();
            return _lines.Table
                .Where(l => l.CompanyId == companyId
                    && l.BankId == bank.Id
                    && l.ExcludedAt == null
                    && l.MatchedGeneralLedgerLineId == null)
                .OrderBy(l => l.PostedDate).ThenBy(l => l.Id)
                .ToList();
        }

        private static void EnsureNotExcluded(BankStatementLine line)
        {
            if (line.ExcludedAt.HasValue)
                throw new InvalidOperationException("That statement line is excluded. Include it before matching.");
        }

        // ------------------------------------------------------------ import helpers

        private enum RowStatus { Ready, Duplicate, Error }

        private sealed record ClassifiedRow(ParsedStatementRow Row, RowStatus Status);

        private static List<ClassifiedRow> Classify(IReadOnlyList<ParsedStatementRow> rows, HashSet<string> existing)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<ClassifiedRow>(rows.Count);
            foreach (var row in rows)
            {
                if (!row.IsValid)
                {
                    result.Add(new ClassifiedRow(row, RowStatus.Error));
                    continue;
                }

                if (row.ExternalId != null && (existing.Contains(row.ExternalId) || !seen.Add(row.ExternalId)))
                {
                    result.Add(new ClassifiedRow(row, RowStatus.Duplicate));
                    continue;
                }
                result.Add(new ClassifiedRow(row, RowStatus.Ready));
            }
            return result;
        }

        /// <summary>
        /// True when the failure is the unique (bank, source, external id) index rejecting a row. Matches the SQL Server
        /// message (index name) and the SQLite message ("UNIQUE constraint failed: BankStatementLine...").
        /// </summary>
        internal static bool IsDuplicateStatementLine(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                var message = current.Message ?? string.Empty;
                if (message.Contains(StatementLineUniqueIndexName, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                    && message.Contains(nameof(BankStatementLine), StringComparison.OrdinalIgnoreCase)
                    && message.Contains(nameof(BankStatementLine.ExternalId), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private HashSet<string> ExistingExternalIds(int bankId, BankStatementSources source)
        {
            var companyId = _tenant.RequireCompanyId();
            return _lines.Table
                .Where(l => l.CompanyId == companyId && l.BankId == bankId && l.Source == source && l.ExternalId != null)
                .Select(l => l.ExternalId!)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static BankStatementPreviewRowDto ToPreviewRow(ClassifiedRow classified) => new()
        {
            RowNumber = classified.Row.RowNumber,
            PostedDate = classified.Row.PostedDate,
            Amount = classified.Row.Amount,
            Payee = classified.Row.Payee,
            Memo = classified.Row.Memo,
            Reference = classified.Row.Reference,
            ExternalId = classified.Row.ExternalId,
            Status = classified.Status switch
            {
                RowStatus.Duplicate => "duplicate",
                RowStatus.Error => "error",
                _ => "ok"
            },
            Error = classified.Row.Error
        };

        private static BankStatementSources ParseSource(BankStatementImportRequest request)
        {
            var source = (BankStatementSources)request.Source;
            if (source is not (BankStatementSources.Csv or BankStatementSources.Ofx))
                throw new InvalidOperationException("Pick a CSV or OFX file.");
            return source;
        }

        private static string NormalizeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return StatusUnmatched;
            var value = status.Trim().ToLowerInvariant();
            return value is StatusUnmatched or StatusMatched or StatusExcluded or StatusAll
                ? value
                : throw new InvalidOperationException("Status must be unmatched, matched, excluded, or all.");
        }

        // ------------------------------------------------------------ lookups / mapping

        private BankStatementLine RequireLine(int id)
        {
            var line = _lines.GetById(id);
            if (line == null || line.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(BankStatementLine), id);
            return line;
        }

        private Bank RequireActiveBank(int bankId)
        {
            var bank = RequireBank(bankId);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("That bank account is archived.");
            return bank;
        }

        private Bank RequireBank(int bankId)
        {
            var bank = _banks.GetById(bankId);
            if (bank == null || bank.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(Bank), bankId);
            return bank;
        }

        private static BankStatementLineDto ToDto(BankStatementLine line, IReadOnlyList<PoolEntry> candidates) => new()
        {
            Id = line.Id,
            BankId = line.BankId,
            BankStatementImportId = line.BankStatementImportId,
            Source = (int)line.Source,
            ExternalId = line.ExternalId,
            PostedDate = line.PostedDate,
            Amount = line.Amount,
            Payee = line.Payee,
            Memo = line.Memo,
            Reference = line.Reference,
            MatchedGeneralLedgerLineId = line.MatchedGeneralLedgerLineId,
            IsMatched = line.MatchedGeneralLedgerLineId.HasValue,
            IsExcluded = line.ExcludedAt.HasValue,
            ExcludedAt = line.ExcludedAt,
            ExcludedBy = line.ExcludedBy,
            ExclusionReason = line.ExclusionReason.HasValue ? (int)line.ExclusionReason.Value : null,
            ExclusionNote = line.ExclusionNote,
            Candidates = candidates.Select(c => new BankStatementMatchCandidateDto
            {
                GeneralLedgerLineId = c.GeneralLedgerLineId,
                GeneralLedgerHeaderId = c.HeaderId,
                Date = c.Date,
                Description = c.Description,
                Amount = c.SignedAmount
            }).ToList()
        };

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static string? TruncateOrNull(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }

        private void EnsureCompany()
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("No company context.");
            _ = _tenant.RequireCompanyId();
        }
    }
}
