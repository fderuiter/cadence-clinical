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
# - BenchmarkTenMillionEdgesTraceLatency (CADENCE_LINEAGE_BENCHMARK=1)
# - BrowserCorrectionReachesTheDatabaseWithItsReason / BrowserCorrection_ReachesTheDatabaseWithItsReason (CADENCE_TEST_BROWSER=1, run in accessibility-e2e)
optin='BenchmarkTenMillionEdgesTraceLatency|BrowserCorrection_?ReachesTheDatabaseWithItsReason'
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
