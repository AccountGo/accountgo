#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Dto.Banking;
using Microsoft.EntityFrameworkCore;
using Services.Banking;
using Services.Financial;

namespace Api.Service;

public sealed class CreateLinkedEntryRequest
{
    public string Kind { get; set; } = "";
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public int WorkBankAccountId { get; set; }
    public int PersonalAccountId { get; set; }
    public decimal? MirrorAmount { get; set; }
    public string? Memo { get; set; }
    public int? StatementLineId { get; set; }
    public int? SplitWorkExpenseAccountId { get; set; }
    public decimal? SplitWorkAmount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed record LinkedEntryFailure(int StatusCode, string Code, string Message);
public sealed record LinkedEntryCreateResult(LinkedEntryPair? Pair, LinkedEntryFailure? Failure, string? Warning = null);

/// <summary>Creates paired journals in one transaction, under each workspace's own tenant and posting policy.</summary>
public sealed class LinkedEntryService
{
    public const string FreelanceIncomeAccountCode = "40900";
    private readonly ApiDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly Services.Financial.IFinancialService _financial;
    private readonly IWorkspacePolicyTransaction _policyTransaction;
    private readonly IWorkspaceApprovalPolicyResolver _approvalResolver;
    private readonly IBankStatementImportService _statements;

    public LinkedEntryService(
        ApiDbContext db,
        ITenantContext tenant,
        Services.Financial.IFinancialService financial,
        IWorkspacePolicyTransaction policyTransaction,
        IWorkspaceApprovalPolicyResolver approvalResolver,
        IBankStatementImportService statements)
    {
        _db = db;
        _tenant = tenant;
        _financial = financial;
        _policyTransaction = policyTransaction;
        _approvalResolver = approvalResolver;
        _statements = statements;
    }

    public LinkedEntryCreateResult Create(int companyId, string ownerUserId, CreateLinkedEntryRequest request)
    {
        var failure = Validate(companyId, ownerUserId, request, out var link, out var workBank, out var personalAccount,
            out var sourceAmount, out var mirrorAmount, out var currencies, out var statementLine);
        if (failure != null) return new(null, failure);

        var requestKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? request.StatementLineId.HasValue ? $"statement:{request.StatementLineId.Value.ToString(CultureInfo.InvariantCulture)}" : null
            : request.IdempotencyKey.Trim();
        if (requestKey != null)
        {
            var previous = _db.LinkedEntryPairs.AsNoTracking().FirstOrDefault(p => p.SourceCompanyId == link!.WorkCompanyId
                && p.OwnerUserId == ownerUserId && p.IdempotencyKey == requestKey);
            if (previous != null) return new(previous, null);
        }

        var duplicateWarning = HasDuplicateBankIdentity(link!.WorkCompanyId, link.PersonalCompanyId, workBank!)
            ? "This account is also in your work books. Importing it in both will count money twice."
            : null;

        LinkedEntryPair? created = null;
        _policyTransaction.ExecuteAcrossCompanies(new[] { link.WorkCompanyId, link.PersonalCompanyId }, _ =>
        {
            var pair = new LinkedEntryPair
            {
                SourceCompanyId = link.WorkCompanyId,
                MirrorCompanyId = link.PersonalCompanyId,
                WorkBankAccountId = workBank!.AccountId!.Value,
                StatementLineId = request.StatementLineId,
                OwnerUserId = ownerUserId,
                Kind = request.Kind,
                SourceAmount = LinkedSourceAmount(request, sourceAmount),
                MirrorAmount = mirrorAmount,
                SourceCurrency = currencies.Source,
                MirrorCurrency = currencies.Mirror,
                ExchangeRate = LinkedSourceAmount(request, sourceAmount) == 0 ? 1m : decimal.Round(mirrorAmount / LinkedSourceAmount(request, sourceAmount), 8),
                State = "PendingSource",
                IdempotencyKey = requestKey,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.LinkedEntryPairs.Add(pair);
            _db.SaveChanges();

            var capitalId = RequireAccountId(link.WorkCompanyId, "30100");
            var drawingsId = RequireAccountId(link.WorkCompanyId, "30800");
            var workLines = BuildWorkLines(request, sourceAmount, workBank!, pair.Id, capitalId, drawingsId);
            JournalEntryHeader source;
            using (_tenant.ImpersonateUser(link.WorkCompanyId, ownerUserId, _db.Companies.IgnoreQueryFilters().Where(c => c.Id == link.WorkCompanyId).Select(c => c.Slug).Single(), CompanyMemberRole.Owner))
            {
                using var tenantWrites = AllowTenantWrites();
                source = NewJournal(link.WorkCompanyId, request, pair, workLines, "Work");
                _financial.AddJournalEntry(source);
                RecordPreparer(source, ownerUserId);
                _db.SaveChanges();
            }

            JournalEntryHeader mirror;
            using (_tenant.ImpersonateUser(link.PersonalCompanyId, ownerUserId, _db.Companies.IgnoreQueryFilters().Where(c => c.Id == link.PersonalCompanyId).Select(c => c.Slug).Single(), CompanyMemberRole.Owner))
            {
                using var tenantWrites = AllowTenantWrites();
                var income = EnsureFreelanceIncomeAccount(link.PersonalCompanyId);
                var mirrorLines = BuildMirrorLines(request.Kind, personalAccount!, income, mirrorAmount);
                mirror = NewJournal(link.PersonalCompanyId, request, pair, mirrorLines, "Personal");
                _financial.AddJournalEntry(mirror);
                RecordPreparer(mirror, ownerUserId);
                _db.SaveChanges();
            }

            pair.SourceJournalId = source.Id;
            pair.MirrorJournalId = mirror.Id;
            _db.SaveChanges();
            created = pair;

            var sourceRole = _tenant.Role ?? CompanyMemberRole.Member;
            var approval = _approvalResolver.Resolve(link.WorkCompanyId, AccessOperationCatalog.FinancialsJournalPost);
            if (approval.Mode == WorkspaceApprovalMode.SingleActorAllowed)
            {
                PostPair(pair, source, mirror, ownerUserId, originalActor: ownerUserId, sourceRole, request.StatementLineId, workBank!.AccountId!.Value);
            }
        });
        return new(created, null, duplicateWarning);
    }

    /// <summary>Runs the normal source posting call and completes its mirror before the ambient pair transaction commits.</summary>
    public bool PostSource(int companyId, int journalId, string actorUserId, CompanyMemberRole actorRole, Action postSource)
    {
        var pair = _db.LinkedEntryPairs.AsNoTracking().FirstOrDefault(p => p.SourceCompanyId == companyId && p.SourceJournalId == journalId);
        if (pair == null)
        {
            var removedState = GetSourcePairState(companyId, journalId);
            if (removedState != null && removedState != "PendingSource")
                throw new LinkedEntryOperationException("linked_entry_not_pending", "The linked entry is no longer pending source posting.");
            return false;
        }
        if (pair.State != "PendingSource")
            throw new LinkedEntryOperationException("linked_entry_not_pending", "The linked entry is no longer pending source posting.");
        _policyTransaction.ExecuteAcrossCompanies(new[] { pair.SourceCompanyId, pair.MirrorCompanyId }, _ =>
        {
            var current = _db.LinkedEntryPairs.Single(p => p.Id == pair.Id);
            if (current.State != "PendingSource")
                throw new LinkedEntryOperationException("linked_entry_not_pending", "The linked entry is no longer pending source posting.");
            {
                using (_tenant.ImpersonateUser(pair.SourceCompanyId, actorUserId,
                    _db.Companies.IgnoreQueryFilters().Where(c => c.Id == pair.SourceCompanyId).Select(c => c.Slug).Single(), actorRole))
                using (_financial.AllowLinkedEntrySourcePosting())
                {
                    postSource();
                }
                var mirror = GetJournal(pair.MirrorCompanyId, pair.MirrorJournalId!.Value);
                using (_tenant.ImpersonateUser(pair.MirrorCompanyId, pair.OwnerUserId,
                    _db.Companies.IgnoreQueryFilters().Where(c => c.Id == pair.MirrorCompanyId).Select(c => c.Slug).Single(), CompanyMemberRole.Owner))
                using (_financial.AllowLinkedEntryMirrorPosting())
                    _financial.UpdateJournalEntry(mirror, posted: true);
                current.State = "Posted";
                _db.SaveChanges();
                if (current.StatementLineId.HasValue)
                {
                    var sourceHeaderId = _db.JournalEntryHeaders.IgnoreQueryFilters()
                        .Where(j => j.CompanyId == current.SourceCompanyId && j.Id == journalId)
                        .Select(j => j.GeneralLedgerHeaderId).Single();
                    var bankLine = _db.GeneralLedgerLines.IgnoreQueryFilters().Single(l => l.CompanyId == current.SourceCompanyId
                        && l.GeneralLedgerHeaderId == sourceHeaderId && l.AccountId == current.WorkBankAccountId);
                    using (_tenant.ImpersonateUser(current.SourceCompanyId, actorUserId,
                        _db.Companies.IgnoreQueryFilters().Where(c => c.Id == current.SourceCompanyId).Select(c => c.Slug).Single(), actorRole))
                        _statements.Match(current.StatementLineId.Value, new MatchBankStatementLineRequest { GeneralLedgerLineId = bankLine.Id });
                }
            }
        });
        return true;
    }

    public bool IsMirrorJournal(int companyId, int journalId) =>
        _db.LinkedEntryPairs.Any(p => p.MirrorCompanyId == companyId && p.MirrorJournalId == journalId);

    public string? GetSourcePairState(int companyId, int journalId)
    {
        var state = _db.LinkedEntryPairs.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.SourceCompanyId == companyId && p.SourceJournalId == journalId)
            .Select(p => p.State).FirstOrDefault();
        if (state != null) return state;

        var removal = _db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.TableName == "__LinkedEntryDraft"
                && a.RecordId == journalId.ToString(CultureInfo.InvariantCulture))
            .OrderByDescending(a => a.Id).Select(a => a.OriginalValue).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(removal)) return null;
        using var json = System.Text.Json.JsonDocument.Parse(removal);
        if (!json.RootElement.TryGetProperty("linkedEntryPairId", out var pairId) || !pairId.TryGetInt32(out var id)) return null;
        return _db.LinkedEntryPairs.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id).Select(p => p.State).SingleOrDefault();
    }

    public IReadOnlyList<LinkedEntryPair> List(int companyId, string ownerUserId) =>
        _db.LinkedEntryPairs.AsNoTracking().Where(p => p.OwnerUserId == ownerUserId &&
            (p.SourceCompanyId == companyId || p.MirrorCompanyId == companyId)).OrderByDescending(p => p.Id).ToList();

    public object GetPersonalAccounts(int workCompanyId, string ownerUserId)
    {
        var link = _db.WorkspaceLinks.AsNoTracking().SingleOrDefault(l => l.WorkCompanyId == workCompanyId && l.OwnerUserId == ownerUserId)
            ?? throw new InvalidOperationException("Linked entries are not available for this workspace.");
        var accounts = _db.Accounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.CompanyId == link.PersonalCompanyId
                && a.ArchivedAt == null && (a.AccountClassId == (int)AccountClasses.Expense ||
                    a.AccountClassId == (int)AccountClasses.Assets && a.IsCash))
            .OrderBy(a => a.AccountCode).Select(a => new { a.Id, a.AccountCode, a.AccountName, a.AccountClassId, a.IsCash }).ToList();
        var workExpenseAccounts = _db.Accounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.CompanyId == workCompanyId
                && a.ArchivedAt == null && a.AccountClassId == (int)AccountClasses.Expense)
            .OrderBy(a => a.AccountCode).Select(a => new { a.Id, a.AccountCode, a.AccountName }).ToList();
        return new
        {
            accounts, workExpenseAccounts,
            personalCurrency = _db.CompanySettings.IgnoreQueryFilters().Where(s => s.CompanyId == link.PersonalCompanyId).Select(s => s.Currency).Single()
        };
    }

    public LinkedEntryCreateResult Reverse(int companyId, string ownerUserId, int pairId, DateTime date, string? reason)
    {
        var pair = _db.LinkedEntryPairs.AsNoTracking().FirstOrDefault(p => p.Id == pairId && p.OwnerUserId == ownerUserId
            && (p.SourceCompanyId == companyId || p.MirrorCompanyId == companyId));
        if (pair == null) return new(null, new(404, "linked_entry_not_found", "Linked entry was not found."));
        if (date == default) return new(null, new(400, "linked_entry_invalid", "Provide a reversal date."));
        if (pair.State == "Reversed" || pair.State == "Cancelled")
            return new(null, new(409, "linked_entry_already_reversed", "This linked entry has already been reversed or cancelled."));
        var periodFailure = CheckOpenYear(pair.SourceCompanyId, date) ?? CheckOpenYear(pair.MirrorCompanyId, date);
        if (periodFailure != null) return new(null, periodFailure);
        if (pair.State != "PendingSource" && _approvalResolver.Resolve(pair.SourceCompanyId, AccessOperationCatalog.FinancialsJournalPost).Mode == WorkspaceApprovalMode.IndependentReviewerRequired)
            return new(null, new(409, "linked_entry_reversal_requires_review", "This workspace requires an independent reviewer to post the reversal."));

        try
        {
            _policyTransaction.ExecuteAcrossCompanies(new[] { pair.SourceCompanyId, pair.MirrorCompanyId }, _ =>
            {
            var current = _db.LinkedEntryPairs.Single(p => p.Id == pairId);
            if (current.State == "PendingSource")
            {
                RemovePendingDrafts(current);
                current.State = "Cancelled";
                _db.SaveChanges();
                return;
            }
            var source = GetJournal(pair.SourceCompanyId, pair.SourceJournalId!.Value);
            var mirror = GetJournal(pair.MirrorCompanyId, pair.MirrorJournalId!.Value);
            if (!source.GeneralLedgerHeaderId.HasValue || !mirror.GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("Both linked journals must be posted before reversal.");
            using (_tenant.ImpersonateUser(pair.SourceCompanyId, ownerUserId,
                _db.Companies.IgnoreQueryFilters().Where(c => c.Id == pair.SourceCompanyId).Select(c => c.Slug).Single(), CompanyMemberRole.Owner))
            {
                using var reversalScope = _financial.AllowLinkedEntryReversal();
                var reversal = _financial.CreateReversingEntry(source.GeneralLedgerHeaderId.Value,
                    $"Linked entry {pair.Id} reversal{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}")}", date.Date);
                foreach (var line in reversal.GeneralLedgerLines) line.CompanyId = pair.SourceCompanyId;
                _financial.SaveGeneralLedgerEntry(reversal);
            }
            using (_tenant.ImpersonateUser(pair.MirrorCompanyId, ownerUserId,
                _db.Companies.IgnoreQueryFilters().Where(c => c.Id == pair.MirrorCompanyId).Select(c => c.Slug).Single(), CompanyMemberRole.Owner))
            {
                using var reversalScope = _financial.AllowLinkedEntryReversal();
                var reversal = _financial.CreateReversingEntry(mirror.GeneralLedgerHeaderId.Value,
                    $"Linked entry {pair.Id} reversal{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}")}", date.Date);
                foreach (var line in reversal.GeneralLedgerLines) line.CompanyId = pair.MirrorCompanyId;
                _financial.SaveGeneralLedgerEntry(reversal);
            }
            current.State = "Reversed";
            _db.SaveChanges();
            });
        }
        catch (LinkedEntryOperationException ex)
        {
            var status = ex.Code == "linked_entry_reversal_requires_review" ? 409 : ex.Code == "linked_entry_permission_required" ? 403 : 409;
            return new(null, new(status, ex.Code, ex.Message));
        }
        return new(_db.LinkedEntryPairs.AsNoTracking().Single(p => p.Id == pairId), null);
    }

    public void CancelPendingSource(int companyId, int journalId)
    {
        var pair = _db.LinkedEntryPairs.FirstOrDefault(p => p.SourceCompanyId == companyId && p.SourceJournalId == journalId
            && p.State == "PendingSource");
        if (pair == null) return;
        _policyTransaction.ExecuteAcrossCompanies(new[] { pair.SourceCompanyId, pair.MirrorCompanyId }, _ =>
        {
            RemovePendingDrafts(pair);
            pair.State = "Cancelled";
            _db.SaveChanges();
        });
    }

    private void RemovePendingDrafts(LinkedEntryPair pair)
    {
        RemovePendingDraft(pair, pair.SourceCompanyId, pair.SourceJournalId);
        RemovePendingDraft(pair, pair.MirrorCompanyId, pair.MirrorJournalId);
    }

    private void RemovePendingDraft(LinkedEntryPair pair, int companyId, int? journalId)
    {
        if (!journalId.HasValue) return;
        var journal = _db.JournalEntryHeaders.IgnoreQueryFilters().Include(j => j.JournalEntryLines)
            .SingleOrDefault(j => j.CompanyId == companyId && j.Id == journalId.Value);
        if (journal == null) return;
        if (journal.Posted == true || journal.GeneralLedgerHeaderId.HasValue)
            throw new InvalidOperationException("A pending linked entry cannot remove a posted journal.");
        var slug = _db.Companies.IgnoreQueryFilters().Where(c => c.Id == companyId).Select(c => c.Slug).Single();
        using (_tenant.ImpersonateUser(companyId, pair.OwnerUserId, slug, CompanyMemberRole.Owner))
        {
            _db.JournalEntryLines.RemoveRange(journal.JournalEntryLines);
            _db.JournalEntryHeaders.Remove(journal);
            _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
            {
                CompanyId = companyId,
                UserName = pair.OwnerUserId,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Deleted,
                TableName = "__LinkedEntryDraft",
                RecordId = journalId.Value.ToString(CultureInfo.InvariantCulture),
                OriginalValue = System.Text.Json.JsonSerializer.Serialize(new { linkedEntryPairId = pair.Id, journalId = journalId.Value })
            });
            _db.SaveChanges();
        }
    }

    public bool IsSourcePair(int companyId, int journalId) =>
        _db.LinkedEntryPairs.Any(p => p.SourceCompanyId == companyId && p.SourceJournalId == journalId && p.State == "PendingSource");

    public LinkedEntryFailure? Validate(int companyId, string ownerUserId, CreateLinkedEntryRequest request,
        out WorkspaceLink? link, out Bank? workBank, out Account? personalAccount, out decimal sourceAmount,
        out decimal mirrorAmount, out (string Source, string Mirror) currencies, out BankStatementLine? statementLine)
    {
        link = _db.WorkspaceLinks.AsNoTracking().FirstOrDefault(l => l.WorkCompanyId == companyId && l.OwnerUserId == ownerUserId);
        workBank = null; personalAccount = null; statementLine = null;
        sourceAmount = 0; mirrorAmount = 0; currencies = ("", "");
        if (link == null) return new(403, "linked_entry_not_available", "Linked entries are not available for this workspace.");
        var personalCompanyId = link.PersonalCompanyId;
        if (string.IsNullOrWhiteSpace(ownerUserId)) return new(403, "linked_entry_not_available", "Only the workspace owner can create a linked entry.");
        var entityType = _db.CompanySettings.IgnoreQueryFilters().Where(s => s.CompanyId == companyId).Select(s => s.EntityType).SingleOrDefault();
        if (entityType != EntityType.SoleProprietor) return new(403, "linked_entry_not_available", "Linked entries are available only for sole proprietor workspaces.");
        if (request == null || request.Date == default || request.Amount <= 0 || request.WorkBankAccountId <= 0 || request.PersonalAccountId <= 0)
            return new(400, "linked_entry_invalid", "Provide a valid date, amount, work bank account, and personal account.");
        if (request.Kind is not ("Personal" or "MoveToPersonal" or "MoneyIn"))
            return new(400, "linked_entry_invalid", "Pick a supported linked entry kind.");
        workBank = _db.Banks.IgnoreQueryFilters().FirstOrDefault(b => b.Id == request.WorkBankAccountId && b.CompanyId == companyId && b.ArchivedAt == null);
        if (workBank?.AccountId == null) return new(400, "linked_entry_invalid", "The work bank account is unavailable.");
        personalAccount = _db.Accounts.IgnoreQueryFilters().FirstOrDefault(a => a.Id == request.PersonalAccountId && a.CompanyId == personalCompanyId && a.ArchivedAt == null);
        if (personalAccount == null) return new(400, "linked_entry_invalid", "The personal account is unavailable.");
        if (request.Kind == "Personal" && personalAccount.AccountClassId != (int)AccountClasses.Expense)
            return new(400, "linked_entry_invalid", "Personal spending must use a household spending category.");
        if (request.Kind != "Personal" && (personalAccount.AccountClassId != (int)AccountClasses.Assets || !personalAccount.IsCash))
            return new(400, "linked_entry_invalid", "Choose a personal bank or cash account.");
        if (request.SplitWorkExpenseAccountId.HasValue)
        {
            var splitAccount = _db.Accounts.IgnoreQueryFilters().FirstOrDefault(a => a.Id == request.SplitWorkExpenseAccountId && a.CompanyId == companyId && a.AccountClassId == (int)AccountClasses.Expense && a.ArchivedAt == null);
            if (request.Kind != "Personal" || splitAccount == null || request.SplitWorkAmount is null || request.SplitWorkAmount < 0 || request.SplitWorkAmount >= request.Amount)
                return new(400, "linked_entry_split_mismatch", "The work and personal portions must be positive and sum to the statement amount.");
        }
        else if (request.SplitWorkAmount.HasValue)
            return new(400, "linked_entry_split_mismatch", "Choose a work expense category for a split.");

        sourceAmount = decimal.Round(request.Amount, 2);
        var sourceCurrency = _db.CompanySettings.IgnoreQueryFilters().Where(s => s.CompanyId == companyId).Select(s => s.Currency).Single();
        var mirrorCurrency = _db.CompanySettings.IgnoreQueryFilters().Where(s => s.CompanyId == personalCompanyId).Select(s => s.Currency).Single();
        currencies = (sourceCurrency, mirrorCurrency);
        var periodFailure = CheckOpenYear(companyId, request.Date) ?? CheckOpenYear(link.PersonalCompanyId, request.Date);
        if (periodFailure != null) return periodFailure;
        var linkedSourceAmount = LinkedSourceAmount(request, sourceAmount);
        if (!string.Equals(sourceCurrency, mirrorCurrency, StringComparison.OrdinalIgnoreCase) && request.MirrorAmount is null)
            return new(400, "linked_entry_mirror_amount_required", "Provide the amount in the personal workspace currency.");
        mirrorAmount = decimal.Round(request.MirrorAmount ?? linkedSourceAmount, 2);
        if (mirrorAmount <= 0) return new(400, "linked_entry_invalid", "The personal currency amount must be positive.");
        if (string.Equals(sourceCurrency, mirrorCurrency, StringComparison.OrdinalIgnoreCase) && mirrorAmount != linkedSourceAmount)
            return new(400, "linked_entry_invalid", "The personal amount must equal the work amount when currencies match.");

        if (request.StatementLineId.HasValue)
        {
            statementLine = _db.BankStatementLines.IgnoreQueryFilters().FirstOrDefault(l => l.Id == request.StatementLineId && l.CompanyId == companyId);
            if (statementLine == null || statementLine.BankId != workBank.Id || statementLine.MatchedGeneralLedgerLineId.HasValue || statementLine.ExcludedAt.HasValue)
                return new(400, "linked_entry_statement_line_invalid", "The bank statement line is unavailable or already handled.");
            if (decimal.Abs(statementLine.Amount) != sourceAmount ||
                (request.Kind == "MoneyIn" ? statementLine.Amount <= 0 : statementLine.Amount >= 0))
                return new(400, "linked_entry_statement_line_invalid", "The linked entry does not match the statement line amount and direction.");
        }
        return null;
    }

    private LinkedEntryFailure? CheckOpenYear(int companyId, DateTime date)
    {
        var companyName = _db.Companies.IgnoreQueryFilters().Where(c => c.Id == companyId).Select(c => c.Name).SingleOrDefault() ?? $"workspace {companyId}";
        var year = _db.FinancialYears.IgnoreQueryFilters().AsNoTracking().FirstOrDefault(y => y.CompanyId == companyId
            && y.StartDate.Date <= date.Date && y.EndDate.Date >= date.Date);
        if (year == null) return new(409, "linked_entry_no_fiscal_year", $"{companyName} has no financial year covering {date:yyyy-MM-dd}.");
        if (!year.IsActive || year.ClosedAt.HasValue) return new(409, "linked_entry_period_closed", $"{companyName} is closed for {date:yyyy-MM-dd}.");
        return null;
    }

    private bool HasDuplicateBankIdentity(int workCompanyId, int personalCompanyId, Bank workBank)
    {
        var banks = _db.Banks.IgnoreQueryFilters().Where(b => b.CompanyId == personalCompanyId && b.ArchivedAt == null);
        return banks.Any(b => (!string.IsNullOrWhiteSpace(workBank.Number) && b.Number == workBank.Number) ||
                              (!string.IsNullOrWhiteSpace(workBank.Iban) && b.Iban == workBank.Iban));
    }

    private static decimal LinkedSourceAmount(CreateLinkedEntryRequest request, decimal sourceAmount) =>
        request.Kind == "Personal" && request.SplitWorkAmount.HasValue
            ? decimal.Round(sourceAmount - request.SplitWorkAmount.Value, 2)
            : sourceAmount;

    private Account EnsureFreelanceIncomeAccount(int companyId)
    {
        var existing = _db.Accounts.IgnoreQueryFilters().FirstOrDefault(a => a.CompanyId == companyId && a.AccountCode == FreelanceIncomeAccountCode);
        if (existing != null) return existing;
        var incomeHeader = _db.Accounts.IgnoreQueryFilters().FirstOrDefault(a => a.CompanyId == companyId && a.AccountCode == "40000")
            ?? _db.Accounts.IgnoreQueryFilters().FirstOrDefault(a => a.CompanyId == companyId && a.AccountClassId == (int)AccountClasses.Revenue && a.ParentAccountId == null)
            ?? throw new InvalidOperationException("The workspace income header account is not configured.");
        var account = new Account
        {
            CompanyId = companyId,
            AccountCode = FreelanceIncomeAccountCode,
            AccountName = "Money from my freelance work",
            Description = "Transfers from the owner's sole proprietor work books.",
            AccountClassId = (int)AccountClasses.Revenue,
            ParentAccountId = incomeHeader.Id,
            DrOrCrSide = DrOrCrSide.Cr,
            IsCash = false,
            IsContraAccount = false
        };
        _db.Accounts.Add(account);
        _db.SaveChanges();
        return account;
    }

    private static JournalEntryHeader NewJournal(int companyId, CreateLinkedEntryRequest request, LinkedEntryPair pair,
        IEnumerable<JournalEntryLine> lines, string side) => new()
    {
        CompanyId = companyId,
        Date = request.Date.Date,
        VoucherType = JournalVoucherTypes.Other,
        Memo = $"Linked entry {pair.Id} ({side}){(string.IsNullOrWhiteSpace(request.Memo) ? "" : $": {request.Memo.Trim()}")}",
        ReferenceNo = $"LE-{pair.Id.ToString(CultureInfo.InvariantCulture)}",
        JournalEntryLines = lines.ToList()
    };

    private static List<JournalEntryLine> BuildWorkLines(CreateLinkedEntryRequest request, decimal amount, Bank bank, int pairId, int capitalId, int drawingsId)
    {
        var lines = new List<JournalEntryLine>();
        if (request.Kind == "MoneyIn")
        {
            lines.Add(Line(bank.AccountId!.Value, DrOrCrSide.Dr, amount, pairId));
            lines.Add(Line(capitalId, DrOrCrSide.Cr, amount, pairId));
        }
        else
        {
            var drawings = amount;
            if (request.SplitWorkExpenseAccountId.HasValue)
            {
                var workAmount = decimal.Round(request.SplitWorkAmount!.Value, 2);
                drawings -= workAmount;
                lines.Add(Line(request.SplitWorkExpenseAccountId.Value, DrOrCrSide.Dr, workAmount, pairId));
            }
            lines.Add(Line(drawingsId, DrOrCrSide.Dr, drawings, pairId));
            lines.Add(Line(bank.AccountId!.Value, DrOrCrSide.Cr, amount, pairId));
        }
        return lines;
    }

    private static List<JournalEntryLine> BuildMirrorLines(string kind, Account personal, Account income, decimal amount) => kind switch
    {
        "MoneyIn" => new() { Line(income.Id, DrOrCrSide.Dr, amount), Line(personal.Id, DrOrCrSide.Cr, amount) },
        _ => new() { Line(personal.Id, DrOrCrSide.Dr, amount), Line(income.Id, DrOrCrSide.Cr, amount) }
    };

    private static JournalEntryLine Line(int accountId, DrOrCrSide side, decimal amount, int? pairId = null) => new()
    {
        AccountId = accountId, DrCr = side, Amount = amount, Memo = pairId.HasValue ? $"Linked entry {pairId.Value.ToString(CultureInfo.InvariantCulture)}" : null
    };

    private void RecordPreparer(JournalEntryHeader journal, string userId) => _db.AuditLogs.Add(new Core.Domain.Auditing.AuditLog
    {
        CompanyId = journal.CompanyId, UserName = userId, AuditEventDateUTC = DateTime.UtcNow,
        AuditEventType = (int)Core.Domain.Auditing.AuditEventTypes.Added, TableName = "__JournalEntryPreparer",
        RecordId = journal.Id.ToString(CultureInfo.InvariantCulture), FieldName = "PreparedByUserId",
        OriginalValue = "", NewValue = userId
    });

    private JournalEntryHeader GetJournal(int companyId, int id) =>
        _db.JournalEntryHeaders.IgnoreQueryFilters().Include(j => j.JournalEntryLines).Single(j => j.CompanyId == companyId && j.Id == id);

    private int RequireAccountId(int companyId, string accountCode) =>
        _db.Accounts.IgnoreQueryFilters().Where(a => a.CompanyId == companyId && a.AccountCode == accountCode)
            .Select(a => (int?)a.Id).SingleOrDefault()
        ?? throw new InvalidOperationException($"The required account {accountCode} is not configured in this workspace.");

    private void PostPair(LinkedEntryPair pair, JournalEntryHeader source, JournalEntryHeader mirror, string ownerUserId,
        string originalActor, CompanyMemberRole sourceRole, int? statementLineId, int workBankAccountId)
    {
        using (_tenant.ImpersonateUser(pair.SourceCompanyId, originalActor,
            _db.Companies.IgnoreQueryFilters().Where(c => c.Id == pair.SourceCompanyId).Select(c => c.Slug).Single(), sourceRole))
        using (AllowTenantWrites())
            _financial.UpdateJournalEntry(source, posted: true);
        using (_tenant.ImpersonateUser(pair.MirrorCompanyId, ownerUserId,
            _db.Companies.IgnoreQueryFilters().Where(c => c.Id == pair.MirrorCompanyId).Select(c => c.Slug).Single(), CompanyMemberRole.Owner))
        using (AllowTenantWrites())
        using (_financial.AllowLinkedEntryMirrorPosting())
            _financial.UpdateJournalEntry(mirror, posted: true);
        pair.State = "Posted";
        _db.SaveChanges();
        if (statementLineId.HasValue)
        {
            var bankLine = _db.GeneralLedgerLines.IgnoreQueryFilters()
                .Where(l => l.CompanyId == pair.SourceCompanyId && l.GeneralLedgerHeaderId == source.GeneralLedgerHeaderId && l.AccountId == workBankAccountId)
                .First();
            using (_tenant.ImpersonateUser(pair.SourceCompanyId, originalActor,
                _db.Companies.IgnoreQueryFilters().Where(c => c.Id == pair.SourceCompanyId).Select(c => c.Slug).Single(), sourceRole))
            using (AllowTenantWrites())
                _statements.Match(statementLineId.Value, new MatchBankStatementLineRequest { GeneralLedgerLineId = bankLine.Id });
        }
    }

    private IDisposable AllowTenantWrites()
    {
        var previous = _db.SuppressTenantSecurityCheck;
        _db.SuppressTenantSecurityCheck = false;
        return new TenantWriteScope(_db, previous);
    }

    private sealed class TenantWriteScope : IDisposable
    {
        private readonly ApiDbContext _db;
        private readonly bool _previous;
        public TenantWriteScope(ApiDbContext db, bool previous) { _db = db; _previous = previous; }
        public void Dispose() => _db.SuppressTenantSecurityCheck = _previous;
    }

}
