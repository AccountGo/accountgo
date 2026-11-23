#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

CONFIG="${CAPTURE_BENCHMARK_CONFIG:-Release}"
WORKER_DLL="${ACCOUNTGO_TEST_OCR_WORKER:-$REPO_ROOT/.build/bin/Api.CaptureWorker/$CONFIG/net10.0/Api.CaptureWorker.dll}"

if [[ ! -f "$WORKER_DLL" ]]; then
  echo "Building Api.CaptureWorker ($CONFIG)..." >&2
  dotnet build src/Api.CaptureWorker/Api.CaptureWorker.csproj -c "$CONFIG" --nologo >&2
fi

if [[ ! -f "$WORKER_DLL" ]]; then
  echo "ERROR: Local OCR worker not found at $WORKER_DLL" >&2
  exit 1
fi

RULES_VERSION="$(sed -n 's/.*public const string RulesVersion = "\([^"]*\)".*/\1/p' src/Api/DocumentCapture/Parsing/DocumentTextFieldParser.cs | head -n 1)"
if [[ -z "$RULES_VERSION" ]]; then
  RULES_VERSION="unknown"
fi

TMP_LOG="$(mktemp)"
trap 'rm -f "$TMP_LOG"' EXIT

FILTER="FullyQualifiedName~LocalOcrSyntheticTests.EnglishImageOcrMeetsSyntheticFieldAccuracyFloor|FullyQualifiedName~DocumentTextFieldParserTests.Parse_SyntheticTruthCleanText_Scores18Of18OnVendorDateAndTotalWithNoTotalsInconsistent"

echo "Running synthetic capture benchmarks (RulesVersion=$RULES_VERSION, Worker=$WORKER_DLL)..." >&2
ACCOUNTGO_TEST_OCR_WORKER="$WORKER_DLL" \
  dotnet test test/AccountGo.BackendTests/AccountGo.BackendTests.csproj \
    -c "$CONFIG" \
    --filter "$FILTER" \
    --logger "console;verbosity=detailed" \
    --nologo | tee "$TMP_LOG" >&2

parse_field() {
  local prefix="$1"
  local field="$2"
  grep -Eo "${prefix} ${field}: [0-9]+/[0-9]+" "$TMP_LOG" | tail -n 1 | awk '{print $NF}'
}

OCR_VENDOR="$(parse_field "English OCR" vendor)"
OCR_DATE="$(parse_field "English OCR" date)"
OCR_TOTAL="$(parse_field "English OCR" total)"

CLEAN_VENDOR="$(parse_field "Clean text" vendor)"
CLEAN_DATE="$(parse_field "Clean text" date)"
CLEAN_TOTAL="$(parse_field "Clean text" total)"

if [[ -z "$OCR_VENDOR" || -z "$OCR_DATE" || -z "$OCR_TOTAL" || -z "$CLEAN_VENDOR" || -z "$CLEAN_DATE" || -z "$CLEAN_TOTAL" ]]; then
  echo "ERROR: Could not parse synthetic field accuracy lines from dotnet test output." >&2
  exit 1
fi

TODAY="$(date -u +%Y-%m-%d)"

awk -v ver="$RULES_VERSION" \
    -v dt="$TODAY" \
    -v ov="$OCR_VENDOR" \
    -v od="$OCR_DATE" \
    -v ot="$OCR_TOTAL" \
    -v cv="$CLEAN_VENDOR" \
    -v cd="$CLEAN_DATE" \
    -v ct="$CLEAN_TOTAL" '
BEGIN {
  split(ov, ova, "/"); split(od, oda, "/"); split(ot, ota, "/");
  ovp = (ova[2] > 0) ? (ova[1] * 100.0 / ova[2]) : 0;
  odp = (oda[2] > 0) ? (oda[1] * 100.0 / oda[2]) : 0;
  otp = (ota[2] > 0) ? (ota[1] * 100.0 / ota[2]) : 0;
  oohit = ova[1] + oda[1] + ota[1];
  ootot = ova[2] + oda[2] + ota[2];
  oop = (ootot > 0) ? (oohit * 100.0 / ootot) : 0;

  split(cv, cva, "/"); split(cd, cda, "/"); split(ct, cta, "/");
  cvp = (cva[2] > 0) ? (cva[1] * 100.0 / cva[2]) : 0;
  cdp = (cda[2] > 0) ? (cda[1] * 100.0 / cda[2]) : 0;
  ctp = (cta[2] > 0) ? (cta[1] * 100.0 / cta[2]) : 0;
  cohit = cva[1] + cda[1] + cta[1];
  cotot = cva[2] + cda[2] + cta[2];
  cop = (cotot > 0) ? (cohit * 100.0 / cotot) : 0;

  printf "| `%s` | %s | OCR (%d): vendor %s (%.1f%%), date %s (%.1f%%), total %s (%.1f%%), overall %d/%d (%.1f%%); clean text (%d): vendor %s (%.1f%%), date %s (%.1f%%), total %s (%.1f%%), overall %d/%d (%.1f%%) | Local OCR synthetic gate (13 EN x 5 variants) + 18 clean-text fixtures (EN+TH) |\n",
    ver, dt, ova[2], ov, ovp, od, odp, ot, otp, oohit, ootot, oop,
    cva[2], cv, cvp, cd, cdp, ct, ctp, cohit, cotot, cop;
}'
