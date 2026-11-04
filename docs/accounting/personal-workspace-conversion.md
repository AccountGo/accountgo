# Personal workspace conversion (retained workspace `personal`)

**Status: EXECUTED on production `personal` (2026-11-04).** Equity-profile conversion verified
(`VERIFY_RESULT pass`). Operating-policy follow-on (Purpose=Personal, CollaborationIntent=WithOthers,
CollaborationState=TeamActive; existing hosted invoicing plan preserved) applied via
`accountgo-ops` workflow `personal-workspace-backfill` / `scripts/personal-workspace-operating-backfill.sql`.
Earlier sections still document the disposable rehearsal that prepared the tooling.

This is a one-time, reviewed, narrowly scoped conversion of a single existing workspace to the Individual accounting
profile. It is not a migration engine and must not be generalised to other workspaces.

Tooling: [`scripts/personal-workspace-conversion.sh`](../../scripts/personal-workspace-conversion.sh) (bash + `psql`,
`pg_dump`, `pg_restore`). Finance treatment: [individual-equity-mapping-decision.md](./individual-equity-mapping-decision.md).

## What the conversion changes

Only these fields, only for the workspace with the given slug:

| Field | New value |
|---|---|
| `CompanySetting.EntityType` | `Individual` (6) |
| `GeneralLedgerSetting.PermanentAccountId` | the workspace's `30100` account |
| `Account` `30100` display name | "Accumulated net worth", only if the current name is a seeded template capital name |
| `Account` `30800` | **Not renamed.** Ordinary personal spending posts as expenses; drawings are excluded from the initial Individual workflow |

It never touches posted journals, general ledger rows, account ids or codes, members, grants, approval rules,
workspace purpose or collaboration state, or other workspaces. Custom account names are kept and reported.
Every write is guarded by the snapshot and reconciled in the same transaction (see below).

**Still open after accounting-profile conversion:** this script deliberately changes only the accounting profile.
After production inspection, the final conversion plan must also address `WorkspaceOperatingSettings.Purpose`,
initialization/`InitializedAtUtc`, collaboration state and any related grants/approval assumptions so the
retained `personal` workspace is a coherent current-model personal workspace — not merely an Individual equity
label on an unclassified or business-shaped operating policy. That follow-on step is out of scope for the
script until inspection results are reviewed.

## Preconditions

All must hold before the apply step. The script enforces the database-side ones; the rest are operator checks.

1. **Finance approval is recorded and deployed.** `IsIndividualMappingApproved` is `true` in the build that is
   serving the workspace. Until then the application deliberately throws for an Individual workspace in year-end
   close, close preview and accounting-policy changes, so converting early would break year-end for that workspace.
   The script cannot verify the deployed build; the operator confirms it from the release SHA and passes the
   approval reference with `--approval-ref`.
2. **Read-only inspection reviewed.** Run `inspect` and `dry-run` against production with an owner/migration role and
   review the output. The conversion is not approved on assumptions about the workspace being empty or simple.
3. **No blockers** from `dry-run`. Blockers (script-enforced) include: not exactly one active workspace with the slug;
   missing `CompanySetting` or `GeneralLedgerSetting`; missing or archived `30100`/`30800`; and any closed fiscal year
   that was closed into a permanent account other than `30100` (history would not match the new profile; accounting
   review required, posted journals are never rewritten).
4. **Warnings understood** and accepted with `--accept-warnings`: a balance on `30300` (stays where it is; future
   closes go to `30100`), non-zero `30700` Opening Balance Equity (reclassify before the next close), a balance on
   `30810` (not closed for Individual), custom account names, and a workspace purpose that is not Personal (this
   conversion does not change purpose; see "Out of scope").
5. **Backup taken and verified** (below), with the Supabase point-in-time-recovery timestamp noted.
6. **Quiet window.** The apply locks the company and settings rows and aborts if the ledger or state differs from the
   snapshot, but a quiet window avoids needless aborts.
7. The connection uses the owner/migration role. The anon/authenticated API roles must not be used.

## Procedure

Set `DATABASE_URL` (libpq URI) in the shell; the script never prints it. Files are written `0600` in a `0700`
directory and contain financial data: treat them as production data and do not paste them into tickets or chat.

```bash
export DATABASE_URL='postgresql://…'          # owner/migration role
S=scripts/personal-workspace-conversion.sh
OUT=./conversion-out

$S inspect  --slug personal                    # read-only inventory; no monetary amounts printed
$S dry-run  --slug personal                    # blockers, warnings, exact before -> after diff; writes nothing
$S backup   --slug personal --out-dir $OUT     # full pg_dump -Fc, verified with pg_restore --list
$S snapshot --slug personal --out-dir $OUT     # before-state + ledger fingerprint
$S apply    --slug personal --out-dir $OUT \
            --backup-file $OUT/<the-backup>.dump \
            --approval-ref '<finance approval reference>' \
            --actor '<operator>' --confirm-write [--accept-warnings]
$S verify   --slug personal --out-dir $OUT     # reconciliation against the snapshot
```

### Dry-run diff

`dry-run` prints one `CHANGE field: before -> after` line per planned write, followed by exactly one
`DRYRUN_RESULT`: `blocked` (exit 3), `needs-accept-warnings`, `ready`, or `already-converted`. Example from the
disposable rehearsal:

```text
CHANGE CompanySetting.EntityType: 1 -> 6
CHANGE Account.30100.AccountName: Owner's Capital -> Accumulated net worth
WARNING account 30800 is left unchanged; ordinary personal spending must post as expenses, not drawings
WARNING workspace purpose is not Personal and is deliberately not changed by this conversion
DRYRUN_RESULT needs-accept-warnings
```

### Backup

- `backup` writes a custom-format `pg_dump` of the whole database and refuses to continue unless `pg_restore --list`
  reads it. Record the Supabase PITR timestamp as the independent recovery point.
- `apply` refuses to run without `--backup-file` pointing at a non-empty file.
- Rehearse the restore on a disposable database before production (commands below).

### Apply and reconciliation

`apply` is one transaction. It locks the company, settings and ledger-settings rows, then aborts with no writes if:

- the snapshot belongs to another workspace, or the workspace state differs from the snapshot (state drift);
- the ledger fingerprint differs from the snapshot (any posting since the snapshot);
- there are blockers, or warnings without `--accept-warnings`.

After writing it reconciles inside the same transaction and rolls everything back on any difference:

- ledger fingerprint equals the snapshot: header and line counts, per-account debit and credit totals, total debits
  equal total credits, and account ids/codes;
- members, grants and approval rules (digest), workspace purpose, collaboration state, policy revision and closed
  fiscal-year count are unchanged;
- the target profile fields hold the planned values.

It then writes one `AuditLog` row (`__PersonalWorkspaceConversion`) with the actor, approval reference and snapshot
digest. Re-running `apply` on an already-converted workspace is a no-op (`APPLY_RESULT already-converted`); re-running
it with a stale snapshot is refused.

### Post-conversion checks (production, after the real apply)

1. `verify` reports `VERIFY_RESULT pass` (it also re-checks that trial-balance debits equal credits).
2. Sign in as the owner and confirm access to `personal` is unchanged.
3. Capture Trial Balance, Balance Sheet and Income Statement for each fiscal year before and after; totals must be
   identical (only the two account labels differ).
4. Open the year-end close **preview** for the latest ended year (read-only). Closing lines must target `30100`
   (Accumulated net worth), must not close `30800` as drawings, and the preview journal must balance.
5. Do not run a real close or reopen on production as a test. That behaviour is verified on a disposable restored copy
   (below) and by the fixture tests in the decision package.

## Restore and reversal

Pick the smallest tool that fits.

| Situation | Action |
|---|---|
| Profile change should be undone, ledger is fine | `restore --snapshot <before-snapshot> --reason '<why>' --confirm-write` |
| Data damaged or restore tool refuses | Restore from the verified dump into a new database and cut over, or use Supabase point-in-time recovery to the recorded timestamp |

`restore` is one transaction: it returns `EntityType` and `PermanentAccountId` to the snapshot values and restores the
two account names only where the conversion wrote them (later user edits are never overwritten). It refuses, with no
writes, if a fiscal year was closed after the conversion or if the entity type was changed outside this conversion
(manual accounting review). It never touches ledger rows. It records its own `AuditLog` row.

### Rehearsing the backup restore on a disposable database

```bash
createdb -h <disposable-host> accountgo_restore_check
pg_restore --no-owner --no-privileges --dbname 'postgresql://…/accountgo_restore_check' $OUT/<the-backup>.dump
DATABASE_URL='postgresql://…/accountgo_restore_check' \
  scripts/personal-workspace-conversion.sh snapshot --slug personal --out-dir /tmp/restore-check
# the restored fingerprint and state must equal the pre-conversion snapshot
```

## Rehearsal evidence (disposable, synthetic data only)

Recorded 2026-11-04 on macOS with PostgreSQL 17 in a local Docker container and `psql`/`pg_dump` 18.4. No production
data or access was used; every amount is invented.

```text
ACCOUNTGO_TEST_POSTGRES="Host=127.0.0.1;Port=5433;Username=postgres;Password=…" \
  dotnet test test/AccountGo.ServicesTests/AccountGo.ServicesTests.csproj \
  --filter "FullyQualifiedName~PersonalWorkspaceConversionScriptTests"
```

Result: 3 passed, 0 failed (skipped automatically when `ACCOUNTGO_TEST_POSTGRES` or the client tools are absent).

| Test | Demonstrates |
|---|---|
| `Conversion_dry_run_apply_verify_restore_and_backup_restore_are_reconciled_and_reversible` | read-only steps change nothing; each missing safeguard refuses and writes nothing; apply converts the profile and labels; ledger lines unchanged; `verify` passes; repeat dry-run is `already-converted`; stale snapshot is refused; fresh-snapshot apply is a no-op; `restore` returns state and fingerprint identical to the original; the pre-conversion dump restores into a fresh database that matches the snapshot |
| `Conversion_is_blocked_when_a_closed_year_was_closed_into_another_permanent_account` | a Corporation-profile workspace with a closed year is blocked by dry-run and refused by apply, with no writes |
| `Apply_aborts_without_writing_when_the_ledger_changed_after_the_snapshot` | a posting after the snapshot aborts apply with no writes |

Not covered by this rehearsal: the actual contents of the real `personal` workspace, Supabase-specific behaviour (PITR,
role permissions, connection poolers), and the application close/reopen flow on a restored production copy.

## Out of scope and open decisions

- **Workspace purpose and collaboration state are not changed.** Setting a purpose on an existing workspace is an owner
  decision outside this conversion; the old legacy-confirmation journey is retired. The dry-run warns when purpose is
  not Personal.
- **Members and grants** are preserved exactly (digest-checked). Whether the existing members belong in a personal
  workspace is an owner decision, not something the script changes.
- **Posted journals are never rewritten** to fit a label. Anything that cannot be reconciled by relabelling is a blocker.
- **Execution is a separate step.** Running `apply` on production needs finance approval of the mapping, a reviewed
  `dry-run` of the real workspace, and operator access that this preparation did not have.
