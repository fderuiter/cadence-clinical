#!/bin/bash
# Fails when a PostgreSQL-backed test did not execute (#5325). The fixtures report a database test as NotExecuted
# when CADENCE_TEST_POSTGRES is missing, so a green run would hide it. The GitHub dotnet-tests job runs this after
# `dotnet test`; the local `cadence ci` applies the same rule (DatabaseTestEvidence).
#
# Usage: scripts/check-database-test-results.sh [repository-root]
set -euo pipefail

root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
cd "$root"

# Opt-in tests skipped by design in dotnet-tests unless their dedicated environment flag is set (DatabaseTestEvidence.OptInTests):
# - BenchmarkTenMillionEdgesTraceLatency / Benchmark_TenMillionEdges_TraceLatency (CADENCE_LINEAGE_BENCHMARK=1)
# - BenchmarkThreeHundredStudyFormsWorklistSqlLatencyAndAllocations / Benchmark_ThreeHundredStudyForms_WorklistSqlLatencyAndAllocations (CADENCE_FORM_QUERY_BENCHMARK=1)
# - BrowserCorrectionReachesTheDatabaseWithItsReason / BrowserCorrection_ReachesTheDatabaseWithItsReason (CADENCE_TEST_BROWSER=1, run in accessibility-e2e)
# During GxP snapshots (CADENCE_GXP_SNAPSHOT=1), BrowserCorrectionReachesTheDatabaseWithItsReason is mandatory (ADR-2531) and must pass in at least one TRX report.
browser_pattern='BrowserCorrection_?ReachesTheDatabaseWithItsReason'
snapshot_mode=0
case "${CADENCE_GXP_SNAPSHOT:-}" in
  1|[Tt][Rr][Uu][Ee]) snapshot_mode=1 ;;
esac

verify_browser_snapshot_evidence() {
  local trx_files=("$@")
  local passed=0
  local failed=0
  local line
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    if printf '%s\n' "$line" | grep -q 'outcome="Passed"'; then
      passed=$((passed + 1))
    elif printf '%s\n' "$line" | grep -Eq 'outcome="(Failed|Error|Timeout|Aborted|Disconnected)"'; then
      failed=$((failed + 1))
    fi
  done < <(sed 's/<UnitTestResult/\n<UnitTestResult/g; s/>/>\n/g' "${trx_files[@]}" 2>/dev/null | grep -F '<UnitTestResult' | grep -E "$browser_pattern" || true)

  if [ "$failed" -gt 0 ] || [ "$passed" -eq 0 ]; then
    echo "::error::Mandatory browser-to-database test BrowserCorrectionReachesTheDatabaseWithItsReason did not pass in TRX evidence (CADENCE_GXP_SNAPSHOT=1 requires accessibility-e2e TRX evidence with CADENCE_TEST_BROWSER=1)."
    exit 1
  fi
}

optin='Benchmark_?TenMillionEdges_?TraceLatency|Benchmark_?ThreeHundredStudyForms_?WorklistSqlLatencyAndAllocations|BrowserCorrection_?ReachesTheDatabaseWithItsReason'

if [ -d "TestResults/gxp" ]; then
  mapfile -t gxp_trx < <(find TestResults/gxp -maxdepth 2 -name '*.trx' 2>/dev/null | sort)
  if [ "${#gxp_trx[@]}" -eq 0 ]; then
    echo "::error::No TRX reports found under TestResults/gxp, so database tests cannot be verified."
    exit 1
  fi

  mapfile -t db_evidence < <(find TestResults/gxp -maxdepth 2 -name '*.database.txt' 2>/dev/null | sort)
  if [ "${#db_evidence[@]}" -eq 0 ]; then
    echo "::error::No *.database.txt fixture evidence found under TestResults/gxp."
    exit 1
  fi

  db_trx=()
  for assembly in "Cadence.IntegrationTests.dll" "Cadence.E2ETests.dll"; do
    matched=0
    for trx in "${gxp_trx[@]}"; do
      if grep -qF "$assembly" "$trx" 2>/dev/null; then
        db_trx+=("$trx")
        matched=1
      fi
    done
    if [ "$matched" -eq 0 ]; then
      echo "::error::No TRX report found for $assembly under TestResults/gxp, so database tests cannot be verified."
      exit 1
    fi
  done

  mapfile -t unique_db_trx < <(printf '%s\n' "${db_trx[@]}" | sort -u)
  if [ "$snapshot_mode" -eq 1 ]; then
    verify_browser_snapshot_evidence "${unique_db_trx[@]}"
  fi

  skipped=$(grep -h 'outcome="NotExecuted"' "${unique_db_trx[@]}" 2>/dev/null | grep -Evc "$optin" || true)
  if [ "$skipped" -gt 0 ]; then
    echo "::error::$skipped PostgreSQL-backed test(s) did not execute. Check that CADENCE_TEST_POSTGRES reaches a PostgreSQL 18 service (docs/CI.md)."
    exit 1
  fi

  echo "All PostgreSQL-backed tests executed (${#unique_db_trx[@]} database TRX report(s) and ${#db_evidence[@]} fixture evidence file(s) checked under TestResults/gxp)."
  exit 0
fi

dirs=(tests/Cadence.IntegrationTests tests/Cadence.E2ETests)

all_dir_trx=()
trx_count=0
for dir in "${dirs[@]}"; do
  mapfile -t dir_trx < <(find "$dir" -name '*.trx' 2>/dev/null | sort)
  if [ "${#dir_trx[@]}" -eq 0 ]; then
    echo "::error::No TRX report found under $dir, so database tests cannot be verified."
    exit 1
  fi
  all_dir_trx+=("${dir_trx[@]}")
  trx_count=$((trx_count + ${#dir_trx[@]}))
done

if [ "$snapshot_mode" -eq 1 ]; then
  verify_browser_snapshot_evidence "${all_dir_trx[@]}"
fi

skipped=$(grep -h 'outcome="NotExecuted"' "${all_dir_trx[@]}" 2>/dev/null | grep -Evc "$optin" || true)
if [ "$skipped" -gt 0 ]; then
  echo "::error::$skipped PostgreSQL-backed test(s) did not execute. Check that CADENCE_TEST_POSTGRES reaches a PostgreSQL 18 service (docs/CI.md)."
  exit 1
fi

echo "All PostgreSQL-backed tests executed ($trx_count TRX report(s) checked)."
