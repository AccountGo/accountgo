# Individual (personal) equity mapping: decision package

**Status: PROPOSED — awaiting finance approval.**
`EntityEquityMapping.IsIndividualMappingApproved` remains `false`. Nothing in this document or its tests changes that.

Personal workspaces stay unavailable in every environment until a finance reviewer records approval and the flag
is changed in a separate, reviewed commit. This package is the evidence the reviewer needs.

## What is proposed

Source of truth in code: `src/Core/Domain/EntityEquityMapping.cs` (`EntityType.Individual`).

| Concern | Proposed treatment |
|---|---|
| Permanent / close-target equity account | `30100` renamed "Personal Capital" |
| Personal drawings (money taken out for personal use) | `30800` renamed "Personal Drawings", debit-natured, closed to `30100` each year |
| Personal contribution (money put in) | Credit `30100` directly; no separate contribution account |
| Retained earnings | Not used. Year profit closes straight into `30100`; `30300` is not the close target |
| Dividends (`30810`) | Not part of the Individual profile and not closed by it |
| Prior-period adjustments (`30820`) | Same as every entity: closed into the permanent account at year end |
| Display-name rename | Only seeded template names are renamed (`IsRenameable`); user-customised names are never overwritten |

The proposal mirrors the existing Sole Proprietor behaviour (30100 close target, 30800 distribution), with
personal labels and no retained-earnings label. This was chosen over the Corporation path so personal books never
imply a legal-entity retained-earnings or dividend concept.

## Opening balance treatment

1. Bank and cash opening balances post Dr bank / Cr `30700` Opening Balance Equity (existing behaviour,
   `BankAccountService.OpeningBalance`).
2. Before the first year close, the owner reclassifies `30700` to `30100`: Dr `30700` / Cr `30100`.
3. Year close is blocked (`opening_balance_equity`) while `30700` is non-zero. Opening balances therefore end up as
   Personal Capital and are never silently swept into profit.
4. Opening liabilities (for example a loan) are posted against `30700` the same way; the net reclassified amount is
   the owner's opening net worth.

## Worked examples

Fixture (disposable, in-memory), calendar FY2025:

| Date | Entry | Dr | Cr | Amount |
|---|---|---|---|---|
| 2025-01-01 | Opening cash | Cash | `30700` OBE | 5,000 |
| 2025-01-02 | Reclassify opening balance | `30700` OBE | `30100` Personal Capital | 5,000 |
| 2025-03-01 | Personal contribution | Cash | `30100` | 1,000 |
| 2025-06-15 | Income | Cash | `40100` Income | 2,000 |
| 2025-07-01 | Expense | `50100` Expense | Cash | 700 |
| 2025-08-01 | Personal drawing | `30800` Personal Drawings | Cash | 300 |

### Year-end close

Expected closing journal (balanced: Dr 2,000 = Cr 2,000):

| Account | Dr | Cr |
|---|---|---|
| `40100` Income | 2,000 | |
| `50100` Expense | | 700 |
| `30800` Personal Drawings | | 300 |
| `30100` Personal Capital | | 1,000 |

Reconciled outcome after close:

- Income, expense, drawings and OBE all net to 0 in the trial balance; `30300` is untouched.
- `30100` closing credit balance is 7,000 (5,000 opening + 1,000 contribution + 2,000 income − 700 expense − 300 drawing).
- Balance sheet: assets 7,000; liabilities 0; equity 7,000, identical to the pre-close balance sheet.
- Income statement for FY2025 is unchanged by the close (closing entries are excluded from P&L views).
- Trial balance debits equal credits.
- Books are locked through 2025-12-31.

### Reopen

Reopen (latest closed year only, reason required) posts an exact reversal of the closing journal (also balanced,
also tagged as closing entries so P&L keeps excluding both legs):

- Income 2,000, expense 700 and drawings 300 are live again; `30100` is back to 6,000.
- Balance sheet totals equal the pre-close totals; the books lock is cleared.
- Re-close reproduces the 7,000 closing capital.

### Reproducing

These examples run as tests on disposable fixtures only:

- `YearEndCloseTests.Individual_close_posts_balanced_closing_journal_to_personal_capital_and_reconciles_reports`
- `YearEndCloseTests.Individual_reopen_reverses_closing_journal_and_restores_pre_close_reports`
- `YearEndCloseTests.Individual_close_is_blocked_until_opening_balance_equity_is_reclassified`
- `YearEndCloseTests.Individual_close_does_not_apply_corporate_dividend_or_retained_earnings_treatment`
- `YearEndCloseTests.Individual_close_and_reopen_stay_closed_without_the_fixture_seam`

```text
dotnet test test/AccountGo.BackendTests/AccountGo.BackendTests.csproj --filter "FullyQualifiedName~YearEndCloseTests|FullyQualifiedName~EntityEquityMappingTests"
```

## Separation from corporate and business treatment

- The Individual profile is a separate `Profiles` entry; `GetProfile` never falls through to Corporation for it.
- Corporation closes to `30300` Retained Earnings and zeroes `30810` Dividends Declared. Individual does neither.
- Behind the gate, an initialised workspace must match its purpose: Personal purpose requires the Individual
  accounting profile, and Business purpose rejects it (`entity_purpose_mismatch`). Neither path can silently
  borrow the other's close behaviour.
- The gate is enforced at three independent points while approval is false:
  `CompanyAccountingPolicyRules.Validate` (create-time policy), `EntityEquityMapping.GetProfile` (provisioning,
  policy changes and year-end close all throw for Individual), and `WorkspaceInitializationScope`
  (Personal purpose is out of scope).
- Test-only seams (`allowUnapproved` on `GetProfile`, plus constructor parameters on `YearEndCloseService` and
  `WorkspaceInitializationApplicator`) exist so disposable fixtures can exercise the behaviour. Dependency
  injection never supplies them.

## Personal solo and household: how they stay gated and reuse team controls

Personal purpose is a separate answer from collaboration (Just me / With others). Both personal journeys share the
business journeys' server controls; nothing personal-specific re-implements them.

- **Gate.** `WorkspaceInitializationScope.IsSupported(purpose, intent, personalReleased)` admits Personal (solo and
  household) only when `PersonalPurposeEnabled && IsIndividualMappingApproved`. `WorkspaceExperienceOptions` also forces
  `PersonalPurposeEnabled` to false while the mapping is unapproved. The request boundary (create, current/initialize)
  and the applicator all use the same check, so enrollment, scope and answer checks still run first.
- **Solo.** Personal + Just me takes the same path as Business + Just me: full workspace access for the owner,
  single-actor journal rule, durable onboarding start and completion.
- **Household.** Personal + With others takes the same path as Business + With others: team setup pending,
  independent-reviewer journal rule by default, owner access preset, and the invitation, reviewer-loss and
  return-to-solo controls from the member and approval-policy work. No separate household logic exists.
- **Accounting profile must match purpose.** After the eligibility proof, initialization rejects with
  `entity_purpose_mismatch` when a Personal workspace is not on the Individual profile or a Business workspace is.
  Creating a Personal workspace with no entity type chosen defaults it to Individual (only reachable once released).
  A signup-created workspace is provisioned before its purpose is known, so a personal continuation needs the
  profile converted first; that is an open dependency, not silently done.
- **Isolation and freezes.** Personal and business remain separate workspaces with no shared ledger. Books locks,
  closed-year controls, balance and repeat-post protections are unchanged because they sit below the journey code.
- **Tests.** `WorkspaceCanaryIsolationTests` covers: closed gate (including `PersonalPurposeEnabled=true` with the
  mapping unapproved), solo and household initialisation behind the released gate on a disposable fixture, and both
  mismatch rejections. The existing HTTP tests that reject Personal while the gate is closed are unchanged and pass.

## Questions that need finance or jurisdiction input

These are genuine open points; the proposal does not guess at them.

1. **Reporting framework.** Which framework (if any) governs personal books? The existing frameworks are business
   oriented. Is a personal "statement of net worth" label preferable to the computed "Retained earnings (opening)"
   and "Profit for the year" balance-sheet lines?
2. **Tax meaning of drawings and contributions.** Treating drawings as an equity movement (not an expense) is a
   bookkeeping convention. Whether any jurisdiction needs these separated for personal tax reporting is open.
3. **Cash vs accrual.** Personal users may expect cash basis. Is Cash basis the default for Individual, and does the
   cash-basis report path need personal-specific sign-off?
4. **Unused accounts.** `30300` Retained Surplus and `30810` Dividends Declared stay in the chart for Individual.
   Hide, rename, or leave? A posting to `30810` is currently not closed for Individual.
5. **Opening balance helper.** Is a guided "reclassify opening balance to Personal Capital" step required, or is
   the manual journal acceptable?
6. **Household (multiple people).** Whether a household shares one `30100`/`30800` pair or needs one drawings
   account per person. The proposal keeps one pair.
7. **Business owner with a personal workspace.** Whether personal and business books of the same person may be
   consolidated for any report. The proposal keeps them as separate workspaces with no shared ledger.
8. **Data retention and fiscal-year conventions** for personal records in each target jurisdiction.

## Approval record

| Field | Value |
|---|---|
| Reviewer | _pending_ |
| Decision | _pending_ |
| Date | _pending_ |
| Changes requested | _pending_ |

Activation also requires, in a separate change: setting `IsIndividualMappingApproved` to `true`, enabling
`WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED`, and enrolling the audience through the existing operator controls.
