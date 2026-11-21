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
# - BrowserCorrectionReachesTheDatabaseWithItsReason / BrowserCorrection_ReachesTheDatabaseWithItsReason (CADENCE_TEST_BROWSER=1, run in accessibility-e2e)
optin='Benchmark_?TenMillionEdges_?TraceLatency|BrowserCorrection_?ReachesTheDatabaseWithItsReason'

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
  skipped=$(grep -h 'outcome="NotExecuted"' "${unique_db_trx[@]}" 2>/dev/null | grep -Evc "$optin" || true)
  if [ "$skipped" -gt 0 ]; then
    echo "::error::$skipped PostgreSQL-backed test(s) did not execute. Check that CADENCE_TEST_POSTGRES reaches a PostgreSQL 18 service (docs/CI.md)."
    exit 1
  fi

  echo "All PostgreSQL-backed tests executed (${#unique_db_trx[@]} database TRX report(s) and ${#db_evidence[@]} fixture evidence file(s) checked under TestResults/gxp)."
  exit 0
fi

dirs=(tests/Cadence.IntegrationTests tests/Cadence.E2ETests)

trx_count=0
for dir in "${dirs[@]}"; do
  dir_trx_count=$(find "$dir" -name '*.trx' 2>/dev/null | wc -l)
  if [ "$dir_trx_count" -eq 0 ]; then
    echo "::error::No TRX report found under $dir, so database tests cannot be verified."
    exit 1
  fi
  trx_count=$((trx_count + dir_trx_count))
done

skipped=$(find "${dirs[@]}" -name '*.trx' -exec grep -h 'outcome="NotExecuted"' {} + 2>/dev/null | grep -Evc "$optin" || true)
if [ "$skipped" -gt 0 ]; then
  echo "::error::$skipped PostgreSQL-backed test(s) did not execute. Check that CADENCE_TEST_POSTGRES reaches a PostgreSQL 18 service (docs/CI.md)."
  exit 1
fi

echo "All PostgreSQL-backed tests executed ($trx_count TRX report(s) checked)."
