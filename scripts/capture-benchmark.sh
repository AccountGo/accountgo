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

echo "Running LocalOcrSyntheticTests (RulesVersion=$RULES_VERSION, Worker=$WORKER_DLL)..." >&2
ACCOUNTGO_TEST_OCR_WORKER="$WORKER_DLL" \
  dotnet test test/AccountGo.BackendTests/AccountGo.BackendTests.csproj \
    -c "$CONFIG" \
    --filter "FullyQualifiedName~LocalOcrSyntheticTests.EnglishImageOcrMeetsSyntheticFieldAccuracyFloor" \
    --logger "console;verbosity=detailed" \
    --nologo | tee "$TMP_LOG" >&2

parse_field() {
  local field="$1"
  grep -Eo "English OCR ${field}: [0-9]+/[0-9]+" "$TMP_LOG" | tail -n 1 | awk '{print $NF}'
}

VENDOR_SCORE="$(parse_field vendor)"
DATE_SCORE="$(parse_field date)"
TOTAL_SCORE="$(parse_field total)"

if [[ -z "$VENDOR_SCORE" || -z "$DATE_SCORE" || -z "$TOTAL_SCORE" ]]; then
  echo "ERROR: Could not parse synthetic field accuracy lines from dotnet test output." >&2
  exit 1
fi

TODAY="$(date -u +%Y-%m-%d)"

awk -v ver="$RULES_VERSION" \
    -v dt="$TODAY" \
    -v v="$VENDOR_SCORE" \
    -v d="$DATE_SCORE" \
    -v t="$TOTAL_SCORE" '
BEGIN {
  split(v, va, "/"); split(d, da, "/"); split(t, ta, "/");
  vp = (va[2] > 0) ? (va[1] * 100.0 / va[2]) : 0;
  dp = (da[2] > 0) ? (da[1] * 100.0 / da[2]) : 0;
  tp = (ta[2] > 0) ? (ta[1] * 100.0 / ta[2]) : 0;
  ohit = va[1] + da[1] + ta[1];
  otot = va[2] + da[2] + ta[2];
  op = (otot > 0) ? (ohit * 100.0 / otot) : 0;
  printf "| `%s` | %s | vendor %s (%.1f%%), date %s (%.1f%%), total %s (%.1f%%), overall %d/%d (%.1f%%) | Local OCR synthetic gate (%d images: 13 EN fixtures x 5 variants) |\n",
    ver, dt, v, vp, d, dp, t, tp, ohit, otot, op, va[2];
}'
