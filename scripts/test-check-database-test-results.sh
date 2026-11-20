#!/bin/bash
# Proves the database-test gate (check-database-test-results.sh) fails on a skipped database test and passes on a
# clean run. Each case builds a throwaway repository tree with synthetic TRX reports; no test is run.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
gate="$here/check-database-test-results.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# A TRX with one passed and one not-executed (skipped) result, the shape a run without CADENCE_TEST_POSTGRES produces.
skipped_trx='<TestRun><Results>
<UnitTestResult testName="Kernel.Passes" outcome="Passed" />
<UnitTestResult testName="Kernel.SkippedWithoutPostgres" outcome="NotExecuted" />
</Results></TestRun>'

passing_trx='<TestRun><Results><UnitTestResult testName="Kernel.Passes" outcome="Passed" /></Results></TestRun>'

benchmark_only_trx='<TestRun><Results>
<UnitTestResult testName="LineageBenchmarkPostgresTests.BenchmarkTenMillionEdgesTraceLatency" outcome="NotExecuted" />
</Results></TestRun>'

browser_optin_trx='<TestRun><Results>
<UnitTestResult testName="BrowserCorrection_ReachesTheDatabaseWithItsReason" outcome="NotExecuted" />
</Results></TestRun>'

# new_tree <name> <integration-trx> [e2e-trx]: writes non-empty reports and prints the tree root.
new_tree() {
  local root="$work/$1"
  mkdir -p "$root/tests/Cadence.IntegrationTests/TestResults" "$root/tests/Cadence.E2ETests/TestResults"
  if [ "${2:-}" != "" ]; then
    printf '%s\n' "$2" > "$root/tests/Cadence.IntegrationTests/TestResults/report_dotnet.trx"
  fi
  if [ "${3:-}" != "" ]; then
    printf '%s\n' "$3" > "$root/tests/Cadence.E2ETests/TestResults/report_dotnet.trx"
  fi
  echo "$root"
}

failures=0

# expect <case> <expected-exit> <tree>: runs the gate and checks its exit status.
expect() {
  local name="$1" want="$2" root="$3" got=0
  bash "$gate" "$root" >/dev/null 2>&1 || got=$?
  if [ "$got" -ne "$want" ]; then
    echo "FAIL: $name: exit $got, expected $want"
    failures=$((failures + 1))
  else
    echo "ok: $name"
  fi
}

expect "fails on a skipped database test" 1 "$(new_tree skipped "$skipped_trx" "$passing_trx")"
expect "passes when every database test executed" 0 "$(new_tree passing "$passing_trx" "$passing_trx")"
expect "ignores the opt-in lineage benchmark" 0 "$(new_tree benchmark "$benchmark_only_trx" "$passing_trx")"
expect "ignores the opt-in browser correction check" 0 "$(new_tree browser_optin "$browser_optin_trx" "$passing_trx")"
expect "fails when Cadence.E2ETests produces no TRX report" 1 "$(new_tree missing_e2e "$passing_trx" "")"
expect "fails when Cadence.IntegrationTests produces no TRX report" 1 "$(new_tree missing_integration "" "$passing_trx")"
expect "fails when no TRX reports exist" 1 "$(mkdir -p "$work/none/tests/Cadence.IntegrationTests" && echo "$work/none")"

if [ "$failures" -ne 0 ]; then
  echo "$failures gate case(s) failed"
  exit 1
fi
echo "All database-test gate cases behaved as expected."
