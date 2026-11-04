# Individual (personal) equity mapping: decision package

**Status: APPROVED and activated for personal purpose (revised model).**

Finance approved a revised Individual mapping for internal personal/household bookkeeping.
`EntityEquityMapping.IsIndividualMappingApproved` is `true`. Personal purpose also requires
`WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED=true`. Conversion of any existing personal workspace
remains unauthorized until inspected separately.

Reports for this release describe personal bookkeeping using recorded balances. They do **not** claim formal
personal-financial-statement compliance merely from this mapping (formal personal reporting can involve
current-value measurements and additional disclosures; see AICPA guidance hosted by FASB,
https://storage.fasb.org/SOP%2082-1.pdf). Net worth as assets minus liabilities follows ordinary personal
bookkeeping practice (e.g. Colorado State University Extension,
https://extension.colostate.edu/resource/net-worth-statements/).

## Approved accounting contract

Source of truth in code: `src/Core/Domain/EntityEquityMapping.cs` (`EntityType.Individual`).

| Item | Approved treatment |
|---|---|
| `30100` | Permanent closing account, displayed as **Accumulated net worth** |
| Income and expenses | Close the annual surplus/deficit into `30100` |
| `30800` Personal Drawings | **Excluded** from the initial personal workflow; do not classify ordinary spending here |
| Opening balances | Record assets and liabilities; reconcile opening equity into `30100`; keep the existing unresolved-opening-equity close blocker |
| Corporate accounts | Do **not** use corporate retained-earnings or dividends-declared treatment for Individual |
| Household | One shared accounting perimeter; member access does not create separate ownership balances |
| Existing records | Preserve historical transactions and account identities; inspect before any conversion |
| Display-name rename | Only seeded template names are renamed (`IsRenameable`); user-customised names are never overwritten |

**Main correction vs the earlier proposal:** do not copy sole-proprietor capital-and-drawings behaviour into
household finances. Routine personal spending must remain visible as expenses.

## Opening balance treatment

1. Bank and cash opening balances post Dr bank / Cr `30700` Opening Balance Equity (existing behaviour,
   `BankAccountService.OpeningBalance`).
2. Before the first year close, the owner reclassifies `30700` to `30100`: Dr `30700` / Cr `30100`.
3. Year close is blocked (`opening_balance_equity`) while `30700` is non-zero. Opening balances therefore end up as
   Accumulated net worth and are never silently swept into profit.
4. Opening liabilities (for example a loan) are posted against `30700` the same way; the net reclassified amount is
   opening net worth (assets minus liabilities).

## Worked close/reopen example

Fixture (disposable, in-memory), calendar FY2025:

| Date | Entry | Dr | Cr | Amount |
|---|---|---|---|---|
| 2025-01-01 | Opening cash | Cash | `30700` OBE | 5,000 |
| 2025-01-02 | Reclassify opening balance | `30700` OBE | `30100` Accumulated net worth | 5,000 |
| 2025-03-01 | Equity top-up / contribution | Cash | `30100` | 1,000 |
| 2025-06-15 | Salary | Cash | `40100` Income | 2,000 |
| 2025-07-01 | Groceries | `50100` Expense | Cash | 700 |

### Year-end close

Expected closing journal (balanced: Dr 2,000 = Cr 2,000):

| Account | Dr | Cr |
|---|---|---|
| `40100` Income | 2,000 | |
| `50100` Expense | | 700 |
| `30100` Accumulated net worth | | 1,300 |

Reconciled outcome after close:

- Income and expense net to 0 in the trial balance; `30800` and `30300` are untouched by the Individual close.
- `30100` closing credit balance is 7,300 (5,000 opening + 1,000 contribution + 2,000 income − 700 expense).
- Balance sheet: assets 7,300; liabilities 0; equity 7,300, identical to the pre-close balance sheet.
- Income statement for FY2025 is unchanged by the close (closing entries are excluded from P&L views).
- Trial balance debits equal credits.
- Books are locked through 2025-12-31.

### Reopen

Reopen (latest closed year only, reason required) posts an exact reversal of the closing journal:

- Income 2,000 and expense 700 are live again; `30100` is back to 6,000.
- Balance sheet totals equal the pre-close totals; the books lock is cleared.
- Re-close reproduces the 7,300 closing Accumulated net worth.

## Required posting examples (tests)

These classification examples are required and covered by `PersonalBookkeepingPostingTests`:

| Example | Treatment |
|---|---|
| Salary | Income |
| Groceries | Expense (not drawings) |
| Checking → savings, or bank → cash wallet | Transfer, not expense |
| Loan proceeds | Liability, not income |
| Loan repayment | Principal reduces liability; interest is expense |
| Credit-card payment | Clears the liability without recording the expense twice |

### Reproducing

```text
dotnet test test/AccountGo.BackendTests/AccountGo.BackendTests.csproj --filter "FullyQualifiedName~YearEndCloseTests.Individual_|FullyQualifiedName~EntityEquityMappingTests|FullyQualifiedName~PersonalBookkeepingPostingTests"
```

Close/reopen tests:

- `YearEndCloseTests.Individual_close_posts_balanced_closing_journal_to_accumulated_net_worth_and_reconciles_reports`
- `YearEndCloseTests.Individual_reopen_reverses_closing_journal_and_restores_pre_close_reports`
- `YearEndCloseTests.Individual_close_is_blocked_until_opening_balance_equity_is_reclassified`
- `YearEndCloseTests.Individual_close_does_not_apply_corporate_dividend_or_retained_earnings_treatment`
- `YearEndCloseTests.Individual_close_and_reopen_stay_closed_without_the_fixture_seam`

## Separation from corporate and business treatment

- The Individual profile is a separate `Profiles` entry; `GetProfile` never falls through to Corporation for it.
- Corporation closes to `30300` Retained Earnings and zeroes `30810` Dividends Declared. Individual does neither.
- Individual does **not** close `30800` drawings.
- Behind the production gate, an initialised workspace must match its purpose: Personal purpose requires the Individual
  accounting profile, and Business purpose rejects it (`entity_purpose_mismatch`).
- With mapping approved, `GetProfile(Individual)` succeeds; personal purpose still needs the env switch.
  `TestApprovalOverride=false` remains available so disposable fixtures can exercise the closed gate.
- Test-only seams (`allowUnapproved` / `TestApprovalOverride`) exist so disposable fixtures can exercise closed-gate behaviour.
  Dependency injection never supplies them.

## Personal solo and household (still gated for production)

Personal purpose is a separate answer from collaboration (Just me / With others). Both personal journeys share the
business journeys' server controls once production activation is separately authorized.

- **Gate.** `WorkspaceInitializationScope.IsSupported` admits Personal only when
  `PersonalPurposeEnabled && IsIndividualMappingApproved` (mapping approved; env switch must be on).
- **Household.** One shared Accumulated net worth perimeter; member access does not create separate ownership balances.
- **Isolation.** Personal and business remain separate workspaces with no shared ledger.

## Open points (not blocking this model approval)

1. **Cash vs accrual default** for Individual books.
2. Whether unused corporate chart rows (`30300`, `30810`, `30800`) should be hidden in personal UI.
3. Guided opening-balance reclassification helper vs manual journal.
4. Whether personal and business books of the same person may ever be consolidated for any report (proposal: no).
5. Data retention and fiscal-year conventions per jurisdiction.

## Approval record

| Field | Value |
|---|---|
| Reviewer | Finance (product counsel direction) |
| Decision | **Approve revised Individual model** for internal personal/household bookkeeping, subject to reconciled posting, reporting and close/reopen tests. Remove routine personal-drawings treatment. Personal purpose may be enabled via `WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED`. **Does not** authorize conversion of the existing personal workspace without inspection. |
| Date | 2026-11-04 |
| Changes requested | Revise away from sole-proprietor capital/drawings; `30100` = Accumulated net worth; exclude `30800` from initial personal workflow; keep OBE close blocker; no corporate RE/dividends; one household perimeter; preserve history; required posting examples above; no formal PFS compliance claim. |

Activation checklist: `IsIndividualMappingApproved=true` (source), `WORKSPACE_EXPERIENCE_PERSONAL_PURPOSE_ENABLED=true`
(live Worker vars), and the usual solo-setup/canary enrollment for create journeys. Conversion of any existing
personal workspace remains unauthorized until inspected under
`docs/accounting/personal-workspace-conversion.md` (purpose and initialization still open after accounting-profile work).
