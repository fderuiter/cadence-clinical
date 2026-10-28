#!/bin/bash
# Fails when a PostgreSQL-backed test did not execute (#5325). The fixtures report a database test as NotExecuted
# when CADENCE_TEST_POSTGRES is missing, so a green run would hide it. The GitHub dotnet-tests job runs this after
# `dotnet test`; the local `cadence ci` applies the same rule (DatabaseTestEvidence).
#
# Usage: scripts/check-database-test-results.sh [repository-root]
set -euo pipefail

root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
cd "$root"

# The opt-in lineage benchmark is skipped by design unless CADENCE_LINEAGE_BENCHMARK=1 (DatabaseTestEvidence.OptInTests).
optin='BenchmarkTenMillionEdgesTraceLatency'
dirs=(tests/Cadence.IntegrationTests tests/Cadence.E2ETests)

trx_count=$(find "${dirs[@]}" -name '*.trx' 2>/dev/null | wc -l)
if [ "$trx_count" -eq 0 ]; then
  echo "::error::No TRX reports found for the integration and E2E tests, so database tests cannot be verified."
  exit 1
fi

skipped=$(find "${dirs[@]}" -name '*.trx' -exec grep -h 'outcome="NotExecuted"' {} + 2>/dev/null | grep -vc "$optin" || true)
if [ "$skipped" -gt 0 ]; then
  echo "::error::$skipped PostgreSQL-backed test(s) did not execute. Check that CADENCE_TEST_POSTGRES reaches a PostgreSQL 18 service (docs/CI.md)."
  exit 1
fi

echo "All PostgreSQL-backed tests executed ($trx_count TRX report(s) checked)."
