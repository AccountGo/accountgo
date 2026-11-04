#!/usr/bin/env bash
# Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
# SPDX-License-Identifier: AGPL-3.0-only
#
# Narrow, rerunnable conversion of ONE existing PostgreSQL workspace (default slug "personal") to the
# Individual accounting profile. It is not a general migration engine.
#
# What it changes (and nothing else):
#   CompanySetting.EntityType                  -> Individual (6)
#   GeneralLedgerSetting.PermanentAccountId    -> the workspace's 30100 account
#   Account 30100 display name                 -> "Accumulated net worth", only when the current name is a
#                                                 seeded template capital name (30800 is not renamed; drawings
#                                                 are excluded from the initial Individual personal workflow)
# It never touches posted journals, general ledger rows, account ids, grants, approval rules, members,
# workspace purpose/collaboration state, or any other workspace.
#
# Modes (see docs/accounting/personal-workspace-conversion.md):
#   inspect   read-only inventory; prints no monetary amounts
#   backup    logical pg_dump of the whole database, verified with pg_restore --list
#   snapshot  writes the before-state + ledger fingerprint JSON used by apply, verify and restore
#   dry-run   prints blockers, warnings and the exact before/after diff; writes nothing
#   apply     one transaction: re-checks the snapshot, converts, reconciles, writes an audit row
#   verify    compares the live database with the snapshot (reconciliation)
#   restore   one transaction: reverts the profile fields to the snapshot values
#
# Connection: DATABASE_URL (libpq URI or conninfo), or the standard PG* environment variables.
# Use an owner/migration role. The anon/authenticated API roles cannot and must not be used.
# The script never prints the connection string. Snapshot/backup files hold financial data: they are
# created 0600 in a 0700 directory and must be handled as production data.

set -euo pipefail

readonly EXIT_USAGE=2
readonly EXIT_BLOCKED=3

usage() {
  cat >&2 <<'USAGE'
usage: personal-workspace-conversion.sh <mode> [options]
  modes:   inspect | backup | snapshot | dry-run | apply | verify | restore
  options:
    --slug SLUG            workspace slug (default: personal)
    --out-dir DIR          output directory for backup/snapshot files (default: ./conversion-out)
    --snapshot FILE        snapshot JSON (default: <out-dir>/<slug>-snapshot.json)
    --backup-file FILE     (apply) path of the verified pg_dump backup taken before conversion
    --approval-ref TEXT    (apply) reference to the recorded finance approval; stored in the audit row
    --reason TEXT          (restore) why the conversion is being reversed; stored in the audit row
    --actor TEXT           name recorded in the audit row (default: $USER)
    --accept-warnings      (apply) proceed although dry-run reported warnings
    --confirm-write        (apply, restore) required acknowledgement that this writes to the database
USAGE
  exit "$EXIT_USAGE"
}

die() { echo "error: $*" >&2; exit "${2:-$EXIT_BLOCKED}"; }

[[ $# -ge 1 ]] || usage
MODE="$1"; shift

SLUG="personal"
OUT_DIR="./conversion-out"
SNAPSHOT_FILE=""
BACKUP_FILE=""
APPROVAL_REF=""
REASON=""
ACTOR="${USER:-operator}"
ACCEPT_WARNINGS="false"
CONFIRM_WRITE="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --slug) SLUG="${2:?}"; shift 2 ;;
    --out-dir) OUT_DIR="${2:?}"; shift 2 ;;
    --snapshot) SNAPSHOT_FILE="${2:?}"; shift 2 ;;
    --backup-file) BACKUP_FILE="${2:?}"; shift 2 ;;
    --approval-ref) APPROVAL_REF="${2:?}"; shift 2 ;;
    --reason) REASON="${2:?}"; shift 2 ;;
    --actor) ACTOR="${2:?}"; shift 2 ;;
    --accept-warnings) ACCEPT_WARNINGS="true"; shift ;;
    --confirm-write) CONFIRM_WRITE="true"; shift ;;
    -h|--help) usage ;;
    *) echo "unknown option: $1" >&2; usage ;;
  esac
done

[[ "$SLUG" =~ ^[a-z0-9][a-z0-9-]{0,99}$ ]] || die "slug must match ^[a-z0-9][a-z0-9-]{0,99}$" "$EXIT_USAGE"
command -v psql >/dev/null || die "psql is required" "$EXIT_USAGE"

CONNECTION_ARGS=()
if [[ -n "${DATABASE_URL:-}" ]]; then
  CONNECTION_ARGS=("$DATABASE_URL")
fi

SNAPSHOT_FILE="${SNAPSHOT_FILE:-$OUT_DIR/$SLUG-snapshot.json}"

prepare_out_dir() {
  umask 077
  mkdir -p "$OUT_DIR"
  chmod 700 "$OUT_DIR"
}

sha256_of() {
  if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

# Runs SQL from stdin. Extra psql variables are passed through "$@".
run_sql() {
  psql ${CONNECTION_ARGS[@]+"${CONNECTION_ARGS[@]}"} -X -q -At -v ON_ERROR_STOP=1 -v slug="$SLUG" \
    -v actor="$ACTOR" -v approval_ref="$APPROVAL_REF" -v reason="$REASON" \
    -v accept_warnings="$ACCEPT_WARNINGS" -v snapshot="${SNAPSHOT_JSON:-null}" "$@" -f -
}

# Shared temporary SQL helpers (pg_temp: nothing is created in the database).
prelude() {
  cat <<'SQL'
SELECT set_config('conv.slug', :'slug', false) AS s1 \gset
SELECT set_config('conv.actor', :'actor', false) AS s2 \gset
SELECT set_config('conv.approval_ref', :'approval_ref', false) AS s3 \gset
SELECT set_config('conv.reason', :'reason', false) AS s4 \gset
SELECT set_config('conv.accept_warnings', :'accept_warnings', false) AS s5 \gset
SELECT set_config('conv.snapshot', :'snapshot', false) AS s6 \gset

CREATE FUNCTION pg_temp.conv_company() RETURNS integer LANGUAGE plpgsql AS $f$
DECLARE n integer; cid integer;
BEGIN
  SELECT count(*), max("Id") INTO n, cid FROM public."Company"
   WHERE "Slug" = current_setting('conv.slug') AND "DeletedAt" IS NULL;
  IF n <> 1 THEN
    RAISE EXCEPTION 'precondition failed: expected exactly one active workspace with slug %, found %', current_setting('conv.slug'), n;
  END IF;
  RETURN cid;
END $f$;

CREATE FUNCTION pg_temp.conv_is_capital_name(n text) RETURNS boolean LANGUAGE sql IMMUTABLE AS $f$
  SELECT lower(n) IN (
    'member capital', 'owner''s capital', 'partners'' capital', 'share capital',
    'personal capital', 'accumulated net worth')
$f$;

CREATE FUNCTION pg_temp.conv_acct(cid integer, code text) RETURNS jsonb LANGUAGE sql AS $f$
  SELECT coalesce(
    (SELECT jsonb_build_object('id', "Id", 'name', "AccountName", 'archived', ("ArchivedAt" IS NOT NULL))
       FROM public."Account" WHERE "CompanyId" = cid AND "AccountCode" = code),
    'null'::jsonb)
$f$;

CREATE FUNCTION pg_temp.conv_net(cid integer, code text) RETURNS numeric LANGUAGE sql AS $f$
  SELECT coalesce(sum(CASE WHEN l."DrCr" = 1 THEN l."Amount" ELSE -l."Amount" END), 0)
    FROM public."GeneralLedgerLine" l JOIN public."Account" a ON a."Id" = l."AccountId"
   WHERE l."CompanyId" = cid AND a."AccountCode" = code
$f$;

-- Access/approval digest: proves grants, members and approval rules are untouched.
CREATE FUNCTION pg_temp.conv_access_digest(cid integer) RETURNS text LANGUAGE sql AS $f$
  SELECT encode(sha256(convert_to((
    SELECT jsonb_build_object(
      'members', (SELECT coalesce(jsonb_agg(jsonb_build_array("Id", "Role", "Status") ORDER BY "Id"), '[]'::jsonb)
                    FROM public."CompanyMember" WHERE "CompanyId" = cid),
      'grants', (SELECT coalesce(jsonb_agg(jsonb_build_array(g."CompanyMemberId", g."SecurityRoleId") ORDER BY g."Id"), '[]'::jsonb)
                   FROM public."CompanyMemberSecurityRole" g JOIN public."CompanyMember" m ON m."Id" = g."CompanyMemberId"
                  WHERE m."CompanyId" = cid),
      'rules', (SELECT coalesce(jsonb_agg(jsonb_build_array("OperationKey", "RuleKind") ORDER BY "OperationKey"), '[]'::jsonb)
                  FROM public."WorkspaceApprovalRule" WHERE "CompanyId" = cid)
    )::text), 'UTF8')), 'hex')
$f$;

CREATE FUNCTION pg_temp.conv_state(cid integer) RETURNS jsonb LANGUAGE sql AS $f$
  SELECT jsonb_build_object(
    'settingRows', (SELECT count(*) FROM public."CompanySetting" WHERE "CompanyId" = cid),
    'glSettingRows', (SELECT count(*) FROM public."GeneralLedgerSetting" WHERE "CompanyId" = cid),
    'entityType', (SELECT max("EntityType") FROM public."CompanySetting" WHERE "CompanyId" = cid),
    'booksLockedThrough', (SELECT max("BooksLockedThrough") FROM public."CompanySetting" WHERE "CompanyId" = cid),
    'permanentAccountId', (SELECT max("PermanentAccountId") FROM public."GeneralLedgerSetting" WHERE "CompanyId" = cid),
    'a30100', pg_temp.conv_acct(cid, '30100'),
    'a30300', pg_temp.conv_acct(cid, '30300'),
    'a30800', pg_temp.conv_acct(cid, '30800'),
    'closedYears', (SELECT count(*) FROM public."FinancialYear" WHERE "CompanyId" = cid AND "ClosedAt" IS NOT NULL),
    'purpose', (SELECT max("Purpose") FROM public."WorkspaceOperatingSettings" WHERE "CompanyId" = cid),
    'collaborationState', (SELECT max("CollaborationState") FROM public."WorkspaceOperatingSettings" WHERE "CompanyId" = cid),
    'policyRevision', (SELECT max("PolicyRevision") FROM public."WorkspaceOperatingSettings" WHERE "CompanyId" = cid),
    'accessDigest', pg_temp.conv_access_digest(cid)
  )
$f$;

-- Ledger fingerprint: per-account totals, row counts and account identity. Names are excluded because
-- the conversion legitimately changes two of them.
CREATE FUNCTION pg_temp.conv_fingerprint(cid integer) RETURNS jsonb LANGUAGE sql AS $f$
  WITH per AS (
    SELECT a."AccountCode" AS code,
           coalesce(sum(l."Amount") FILTER (WHERE l."DrCr" = 1), 0) AS dr,
           coalesce(sum(l."Amount") FILTER (WHERE l."DrCr" = 2), 0) AS cr
      FROM public."GeneralLedgerLine" l JOIN public."Account" a ON a."Id" = l."AccountId"
     WHERE l."CompanyId" = cid
     GROUP BY a."AccountCode")
  SELECT jsonb_build_object(
    'headers', (SELECT count(*) FROM public."GeneralLedgerHeader" WHERE "CompanyId" = cid),
    'lines', (SELECT count(*) FROM public."GeneralLedgerLine" WHERE "CompanyId" = cid),
    'debit', (SELECT coalesce(sum(dr), 0) FROM per),
    'credit', (SELECT coalesce(sum(cr), 0) FROM per),
    'byAccount', (SELECT coalesce(jsonb_agg(jsonb_build_array(code, dr, cr) ORDER BY code), '[]'::jsonb) FROM per),
    'accounts', (SELECT coalesce(jsonb_agg(jsonb_build_array("Id", "AccountCode") ORDER BY "AccountCode", "Id"), '[]'::jsonb)
                   FROM public."Account" WHERE "CompanyId" = cid))
$f$;

-- Blockers stop the conversion, warnings need --accept-warnings, changes are the exact planned diff.
CREATE FUNCTION pg_temp.conv_assess(cid integer) RETURNS jsonb LANGUAGE plpgsql AS $f$
DECLARE
  st jsonb := pg_temp.conv_state(cid);
  blockers jsonb := '[]'::jsonb;
  warnings jsonb := '[]'::jsonb;
  changes jsonb := '[]'::jsonb;
  cap jsonb := st -> 'a30100';
  drw jsonb := st -> 'a30800';
  cap_id integer;
BEGIN
  IF (st ->> 'settingRows')::int <> 1 THEN blockers := blockers || to_jsonb('expected exactly one CompanySetting row'::text); END IF;
  IF (st ->> 'glSettingRows')::int <> 1 THEN blockers := blockers || to_jsonb('expected exactly one GeneralLedgerSetting row'::text); END IF;
  IF cap = 'null'::jsonb THEN blockers := blockers || to_jsonb('account 30100 does not exist'::text); END IF;
  IF drw = 'null'::jsonb THEN blockers := blockers || to_jsonb('account 30800 does not exist'::text); END IF;
  IF cap <> 'null'::jsonb AND (cap ->> 'archived')::boolean THEN blockers := blockers || to_jsonb('account 30100 is archived'::text); END IF;
  IF drw <> 'null'::jsonb AND (drw ->> 'archived')::boolean THEN blockers := blockers || to_jsonb('account 30800 is archived'::text); END IF;

  IF cap <> 'null'::jsonb THEN
    cap_id := (cap ->> 'id')::int;
    IF (st ->> 'closedYears')::int > 0 AND (st ->> 'permanentAccountId') IS DISTINCT FROM cap_id::text THEN
      blockers := blockers || to_jsonb('closed fiscal years were closed into a different permanent account; manual accounting review required'::text);
    END IF;
  END IF;

  IF (st ->> 'entityType') IS DISTINCT FROM '6' THEN
    changes := changes || jsonb_build_object('field', 'CompanySetting.EntityType', 'before', st -> 'entityType', 'after', 6);
  END IF;
  IF cap_id IS NOT NULL AND (st ->> 'permanentAccountId') IS DISTINCT FROM cap_id::text THEN
    changes := changes || jsonb_build_object('field', 'GeneralLedgerSetting.PermanentAccountId', 'before', st -> 'permanentAccountId', 'after', cap_id);
  END IF;
  IF cap <> 'null'::jsonb AND (cap ->> 'name') <> 'Accumulated net worth' THEN
    IF pg_temp.conv_is_capital_name(cap ->> 'name') THEN
      changes := changes || jsonb_build_object('field', 'Account.30100.AccountName', 'before', cap -> 'name', 'after', 'Accumulated net worth');
    ELSE
      warnings := warnings || to_jsonb('account 30100 has a custom name that is kept'::text);
    END IF;
  END IF;
  -- 30800 stays as-is: Individual close does not treat drawings as a distribution account.
  IF drw <> 'null'::jsonb THEN
    warnings := warnings || to_jsonb('account 30800 is left unchanged; ordinary personal spending must post as expenses, not drawings'::text);
  END IF;

  IF pg_temp.conv_net(cid, '30300') <> 0 THEN
    warnings := warnings || to_jsonb('account 30300 carries a balance; it is not rewritten and future closes go to 30100'::text);
  END IF;
  IF pg_temp.conv_net(cid, '30700') <> 0 THEN
    warnings := warnings || to_jsonb('account 30700 Opening Balance Equity is not zero; reclassify it to 30100 before the next year close'::text);
  END IF;
  IF pg_temp.conv_net(cid, '30810') <> 0 THEN
    warnings := warnings || to_jsonb('account 30810 carries a balance; the Individual profile does not close it'::text);
  END IF;
  IF coalesce(st ->> 'purpose', '0') <> '1' THEN
    warnings := warnings || to_jsonb('workspace purpose is not Personal and is deliberately not changed by this conversion'::text);
  END IF;

  RETURN jsonb_build_object('blockers', blockers, 'warnings', warnings, 'changes', changes);
END $f$;
SQL
}

require_snapshot() {
  [[ -s "$SNAPSHOT_FILE" ]] || die "snapshot file not found: $SNAPSHOT_FILE (run the snapshot mode first)"
  SNAPSHOT_JSON="$(cat "$SNAPSHOT_FILE")"
  export SNAPSHOT_JSON
}

case "$MODE" in
  inspect)
    { prelude; cat <<'SQL'
SELECT 'workspace slug=' || current_setting('conv.slug') || ' companyId=' || pg_temp.conv_company();
SELECT jsonb_pretty(pg_temp.conv_state(pg_temp.conv_company()));
SELECT 'ledger rows: headers=' || (f ->> 'headers') || ' lines=' || (f ->> 'lines') || ' balanced=' || ((f ->> 'debit') = (f ->> 'credit'))
  FROM (SELECT pg_temp.conv_fingerprint(pg_temp.conv_company()) AS f) x;
SELECT 'members(active)=' || count(*) FROM public."CompanyMember" WHERE "CompanyId" = pg_temp.conv_company() AND "Status" = 1;
SELECT 'approval rule ' || "OperationKey" || ' kind=' || "RuleKind" FROM public."WorkspaceApprovalRule" WHERE "CompanyId" = pg_temp.conv_company() ORDER BY 1;
SELECT 'fiscal year ' || "FiscalYearCode" || ' closed=' || ("ClosedAt" IS NOT NULL) FROM public."FinancialYear" WHERE "CompanyId" = pg_temp.conv_company() ORDER BY "StartDate";
SELECT 'assessment: ' || jsonb_pretty(pg_temp.conv_assess(pg_temp.conv_company()));
SQL
    } | run_sql
    ;;

  backup)
    command -v pg_dump >/dev/null || die "pg_dump is required" "$EXIT_USAGE"
    command -v pg_restore >/dev/null || die "pg_restore is required" "$EXIT_USAGE"
    prepare_out_dir
    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    target="$OUT_DIR/$SLUG-backup-$stamp.dump"
    pg_dump ${CONNECTION_ARGS[@]+"${CONNECTION_ARGS[@]}"} --format=custom --no-owner --no-privileges --file="$target"
    chmod 600 "$target"
    entries="$(pg_restore --list "$target" | grep -c . || true)"
    [[ "$entries" -gt 0 ]] || die "backup verification failed: pg_restore --list returned no entries"
    echo "backup written: $target"
    echo "backup entries: $entries"
    echo "backup sha256: $(sha256_of "$target")"
    ;;

  snapshot)
    prepare_out_dir
    tmp="$(mktemp "$OUT_DIR/.snapshot.XXXXXX")"
    { prelude; cat <<'SQL'
SELECT jsonb_pretty(jsonb_build_object(
  'schema', 1,
  'slug', current_setting('conv.slug'),
  'companyId', pg_temp.conv_company(),
  'capturedAtUtc', to_char(now() AT TIME ZONE 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
  'state', pg_temp.conv_state(pg_temp.conv_company()),
  'fingerprint', pg_temp.conv_fingerprint(pg_temp.conv_company()),
  'assessment', pg_temp.conv_assess(pg_temp.conv_company())));
SQL
    } | run_sql > "$tmp"
    mv "$tmp" "$SNAPSHOT_FILE"
    chmod 600 "$SNAPSHOT_FILE"
    echo "snapshot written: $SNAPSHOT_FILE"
    echo "snapshot sha256: $(sha256_of "$SNAPSHOT_FILE")"
    ;;

  dry-run)
    output="$( { prelude; cat <<'SQL'
SELECT 'workspace slug=' || current_setting('conv.slug') || ' companyId=' || pg_temp.conv_company();
SELECT 'BLOCKER ' || b FROM jsonb_array_elements_text((pg_temp.conv_assess(pg_temp.conv_company())) -> 'blockers') b;
SELECT 'WARNING ' || w FROM jsonb_array_elements_text((pg_temp.conv_assess(pg_temp.conv_company())) -> 'warnings') w;
SELECT format('CHANGE %s: %s -> %s', c ->> 'field', c ->> 'before', c ->> 'after')
  FROM jsonb_array_elements((pg_temp.conv_assess(pg_temp.conv_company())) -> 'changes') c;
SELECT 'DRYRUN_RESULT ' || CASE
  WHEN jsonb_array_length(a -> 'blockers') > 0 THEN 'blocked'
  WHEN jsonb_array_length(a -> 'changes') = 0 THEN 'already-converted'
  WHEN jsonb_array_length(a -> 'warnings') > 0 THEN 'needs-accept-warnings'
  ELSE 'ready' END
  FROM (SELECT pg_temp.conv_assess(pg_temp.conv_company()) AS a) x;
SQL
    } | run_sql )"
    echo "$output"
    if grep -q '^DRYRUN_RESULT blocked$' <<<"$output"; then exit "$EXIT_BLOCKED"; fi
    ;;

  apply)
    [[ "$CONFIRM_WRITE" == "true" ]] || die "apply writes to the database; pass --confirm-write" "$EXIT_USAGE"
    [[ -n "$APPROVAL_REF" ]] || die "apply requires --approval-ref (reference to the recorded finance approval)" "$EXIT_USAGE"
    [[ -n "$BACKUP_FILE" && -s "$BACKUP_FILE" ]] || die "apply requires --backup-file pointing at a non-empty backup taken by the backup mode"
    require_snapshot
    snapshot_digest="$(sha256_of "$SNAPSHOT_FILE")"
    { prelude; cat <<SQL
SELECT set_config('conv.snapshot_digest', '$snapshot_digest', false) AS s7 \\gset
SQL
      cat <<'SQL'
DO $apply$
DECLARE
  cid integer := pg_temp.conv_company();
  snap jsonb := current_setting('conv.snapshot')::jsonb;
  before_state jsonb;
  after_state jsonb;
  asm jsonb;
  cap_id integer;
  drw_id integer;
  rows integer;
BEGIN
  PERFORM 1 FROM public."Company" WHERE "Id" = cid FOR UPDATE;
  PERFORM 1 FROM public."CompanySetting" WHERE "CompanyId" = cid FOR UPDATE;
  PERFORM 1 FROM public."GeneralLedgerSetting" WHERE "CompanyId" = cid FOR UPDATE;

  IF (snap ->> 'companyId')::int <> cid OR (snap ->> 'slug') <> current_setting('conv.slug') THEN
    RAISE EXCEPTION 'snapshot belongs to a different workspace';
  END IF;
  before_state := pg_temp.conv_state(cid);
  IF before_state <> snap -> 'state' THEN
    RAISE EXCEPTION 'workspace state changed since the snapshot; take a new snapshot and repeat the dry-run';
  END IF;
  IF pg_temp.conv_fingerprint(cid) <> snap -> 'fingerprint' THEN
    RAISE EXCEPTION 'ledger changed since the snapshot; take a new snapshot and repeat the dry-run';
  END IF;

  asm := pg_temp.conv_assess(cid);
  IF jsonb_array_length(asm -> 'blockers') > 0 THEN RAISE EXCEPTION 'blocked: %', asm -> 'blockers'; END IF;
  IF jsonb_array_length(asm -> 'warnings') > 0 AND current_setting('conv.accept_warnings') <> 'true' THEN
    RAISE EXCEPTION 'warnings present; review the dry-run and pass --accept-warnings: %', asm -> 'warnings';
  END IF;
  IF jsonb_array_length(asm -> 'changes') = 0 THEN
    RAISE NOTICE 'APPLY_RESULT already-converted (no changes written)';
    RETURN;
  END IF;

  cap_id := (before_state -> 'a30100' ->> 'id')::int;

  UPDATE public."CompanySetting" SET "EntityType" = 6 WHERE "CompanyId" = cid AND "EntityType" <> 6;
  UPDATE public."GeneralLedgerSetting" SET "PermanentAccountId" = cap_id
   WHERE "CompanyId" = cid AND "PermanentAccountId" IS DISTINCT FROM cap_id;
  UPDATE public."Account" SET "AccountName" = 'Accumulated net worth'
   WHERE "Id" = cap_id AND "AccountName" <> 'Accumulated net worth' AND pg_temp.conv_is_capital_name("AccountName");

  -- Reconciliation inside the same transaction: any difference aborts and rolls everything back.
  IF pg_temp.conv_fingerprint(cid) <> snap -> 'fingerprint' THEN
    RAISE EXCEPTION 'reconciliation failed: ledger fingerprint differs after conversion';
  END IF;
  after_state := pg_temp.conv_state(cid);
  IF after_state -> 'accessDigest' <> before_state -> 'accessDigest'
     OR after_state -> 'purpose' <> before_state -> 'purpose'
     OR after_state -> 'collaborationState' <> before_state -> 'collaborationState'
     OR after_state -> 'policyRevision' <> before_state -> 'policyRevision'
     OR after_state -> 'closedYears' <> before_state -> 'closedYears' THEN
    RAISE EXCEPTION 'reconciliation failed: access, approval, purpose or fiscal-year state changed';
  END IF;
  IF (after_state ->> 'entityType') <> '6' OR (after_state ->> 'permanentAccountId')::int <> cap_id THEN
    RAISE EXCEPTION 'reconciliation failed: target profile not applied';
  END IF;

  INSERT INTO public."AuditLog" ("CompanyId", "UserName", "AuditEventDateUTC", "AuditEventType", "TableName", "RecordId", "FieldName", "OriginalValue", "NewValue")
  VALUES (cid, current_setting('conv.actor'), now() AT TIME ZONE 'utc', 3, '__PersonalWorkspaceConversion', cid::text, 'apply',
          jsonb_build_object('entityType', before_state -> 'entityType', 'permanentAccountId', before_state -> 'permanentAccountId')::text,
          jsonb_build_object('approvalRef', current_setting('conv.approval_ref'),
                             'snapshotSha256', current_setting('conv.snapshot_digest'),
                             'changes', asm -> 'changes')::text);
  RAISE NOTICE 'APPLY_RESULT converted (% planned changes)', jsonb_array_length(asm -> 'changes');
END $apply$;
SQL
    } | run_sql
    ;;

  verify)
    require_snapshot
    result="$( { prelude; cat <<'SQL'
WITH ctx AS (
  SELECT pg_temp.conv_company() AS cid, current_setting('conv.snapshot')::jsonb AS snap
), cur AS (
  SELECT cid, snap, pg_temp.conv_state(cid) AS st, pg_temp.conv_fingerprint(cid) AS fp FROM ctx
), checks AS (
  SELECT 'entity type is Individual' AS name, (st ->> 'entityType') = '6' AS ok FROM cur
  UNION ALL SELECT 'permanent account is 30100',
    (st ->> 'permanentAccountId') IS NOT DISTINCT FROM (st -> 'a30100' ->> 'id') FROM cur
  UNION ALL SELECT 'account 30100 label is Accumulated net worth or an untouched custom name',
    (st -> 'a30100' ->> 'name') = 'Accumulated net worth' OR (st -> 'a30100' ->> 'name') = (snap -> 'state' -> 'a30100' ->> 'name') FROM cur
  UNION ALL SELECT 'account 30800 label is unchanged (drawings excluded from Individual workflow)',
    (st -> 'a30800' ->> 'name') IS NOT DISTINCT FROM (snap -> 'state' -> 'a30800' ->> 'name') FROM cur
  UNION ALL SELECT 'ledger fingerprint equals snapshot (rows, per-account totals, account ids)',
    fp = snap -> 'fingerprint' FROM cur
  UNION ALL SELECT 'trial balance debits equal credits', (fp ->> 'debit') = (fp ->> 'credit') FROM cur
  UNION ALL SELECT 'members, grants and approval rules unchanged',
    st -> 'accessDigest' = snap -> 'state' -> 'accessDigest' FROM cur
  UNION ALL SELECT 'purpose, collaboration state and policy revision unchanged',
    st -> 'purpose' IS NOT DISTINCT FROM snap -> 'state' -> 'purpose'
    AND st -> 'collaborationState' IS NOT DISTINCT FROM snap -> 'state' -> 'collaborationState'
    AND st -> 'policyRevision' IS NOT DISTINCT FROM snap -> 'state' -> 'policyRevision' FROM cur
  UNION ALL SELECT 'closed fiscal years unchanged', st -> 'closedYears' = snap -> 'state' -> 'closedYears' FROM cur
)
SELECT (CASE WHEN ok THEN 'PASS ' ELSE 'FAIL ' END) || name FROM checks
UNION ALL SELECT 'VERIFY_RESULT ' || CASE WHEN bool_and(ok) THEN 'pass' ELSE 'fail' END FROM checks;
SQL
    } | run_sql )"
    echo "$result"
    grep -q '^VERIFY_RESULT pass$' <<<"$result" || exit "$EXIT_BLOCKED"
    ;;

  restore)
    [[ "$CONFIRM_WRITE" == "true" ]] || die "restore writes to the database; pass --confirm-write" "$EXIT_USAGE"
    [[ -n "$REASON" ]] || die "restore requires --reason" "$EXIT_USAGE"
    require_snapshot
    { prelude; cat <<'SQL'
DO $restore$
DECLARE
  cid integer := pg_temp.conv_company();
  snap jsonb := current_setting('conv.snapshot')::jsonb;
  old_state jsonb := snap -> 'state';
  cur_state jsonb;
  after_state jsonb;
  old_entity integer := (snap -> 'state' ->> 'entityType')::int;
  old_permanent integer := (snap -> 'state' ->> 'permanentAccountId')::int;
  cap_id integer := (snap -> 'state' -> 'a30100' ->> 'id')::int;
BEGIN
  PERFORM 1 FROM public."Company" WHERE "Id" = cid FOR UPDATE;
  PERFORM 1 FROM public."CompanySetting" WHERE "CompanyId" = cid FOR UPDATE;
  PERFORM 1 FROM public."GeneralLedgerSetting" WHERE "CompanyId" = cid FOR UPDATE;

  IF (snap ->> 'companyId')::int <> cid OR (snap ->> 'slug') <> current_setting('conv.slug') THEN
    RAISE EXCEPTION 'snapshot belongs to a different workspace';
  END IF;
  cur_state := pg_temp.conv_state(cid);

  IF (cur_state ->> 'closedYears')::int > (old_state ->> 'closedYears')::int THEN
    RAISE EXCEPTION 'a fiscal year was closed after the conversion; reversal needs manual accounting review';
  END IF;
  IF (cur_state ->> 'entityType')::int NOT IN (6, old_entity) THEN
    RAISE EXCEPTION 'entity type was changed outside this conversion; reversal needs manual review';
  END IF;

  UPDATE public."CompanySetting" SET "EntityType" = old_entity WHERE "CompanyId" = cid AND "EntityType" <> old_entity;
  UPDATE public."GeneralLedgerSetting" SET "PermanentAccountId" = old_permanent
   WHERE "CompanyId" = cid AND "PermanentAccountId" IS DISTINCT FROM old_permanent;
  -- Names are restored only where the conversion wrote them, so later user edits are never clobbered.
  UPDATE public."Account" SET "AccountName" = old_state -> 'a30100' ->> 'name'
   WHERE "Id" = cap_id AND "AccountName" = 'Accumulated net worth' AND "AccountName" <> old_state -> 'a30100' ->> 'name';

  after_state := pg_temp.conv_state(cid);
  IF after_state -> 'accessDigest' <> cur_state -> 'accessDigest'
     OR after_state -> 'purpose' <> cur_state -> 'purpose'
     OR after_state -> 'policyRevision' <> cur_state -> 'policyRevision' THEN
    RAISE EXCEPTION 'restore changed access, purpose or policy state';
  END IF;
  IF (after_state ->> 'entityType')::int <> old_entity
     OR (after_state ->> 'permanentAccountId')::int IS DISTINCT FROM old_permanent THEN
    RAISE EXCEPTION 'restore did not return the profile fields to the snapshot values';
  END IF;

  INSERT INTO public."AuditLog" ("CompanyId", "UserName", "AuditEventDateUTC", "AuditEventType", "TableName", "RecordId", "FieldName", "OriginalValue", "NewValue")
  VALUES (cid, current_setting('conv.actor'), now() AT TIME ZONE 'utc', 3, '__PersonalWorkspaceConversion', cid::text, 'restore',
          jsonb_build_object('entityType', cur_state -> 'entityType', 'permanentAccountId', cur_state -> 'permanentAccountId')::text,
          jsonb_build_object('reason', current_setting('conv.reason'))::text);
  RAISE NOTICE 'RESTORE_RESULT reverted to snapshot profile (ledger rows were not touched)';
END $restore$;
SQL
    } | run_sql
    ;;

  *)
    usage
    ;;
esac
