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
<UnitTestResult testName="Benchmark_TenMillionEdges_TraceLatency" outcome="NotExecuted" />
</Results></TestRun>'

benchmark_display_name_trx='<TestRun><Results>
<UnitTestResult testName="Benchmark_TenMillionEdges_TraceLatency" outcome="NotExecuted" />
</Results></TestRun>'

browser_optin_trx='<TestRun><Results>
<UnitTestResult testName="BrowserCorrection_ReachesTheDatabaseWithItsReason" outcome="NotExecuted" />
</Results></TestRun>'

single_line_optin_plus_mandatory_skip_trx='<TestRun><Results><UnitTestResult testName="Benchmark_TenMillionEdges_TraceLatency" outcome="NotExecuted" /><UnitTestResult testName="BrowserCorrection_ReachesTheDatabaseWithItsReason" outcome="NotExecuted" /><UnitTestResult testName="Kernel.SkippedWithoutPostgres" outcome="NotExecuted" /></Results></TestRun>'

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
expect "ignores the opt-in lineage benchmark DisplayName" 0 "$(new_tree benchmark_display "$benchmark_display_name_trx" "$passing_trx")"
expect "ignores the opt-in browser correction check" 0 "$(new_tree browser_optin "$browser_optin_trx" "$passing_trx")"
expect "fails on single-line xunit.v3 TRX combining opt-in skips with mandatory database skip" 1 "$(new_tree single_line_mixed "$single_line_optin_plus_mandatory_skip_trx" "$passing_trx")"
expect "fails when Cadence.E2ETests produces no TRX report" 1 "$(new_tree missing_e2e "$passing_trx" "")"
expect "fails when Cadence.IntegrationTests produces no TRX report" 1 "$(new_tree missing_integration "" "$passing_trx")"
expect "fails when no TRX reports exist" 1 "$(mkdir -p "$work/none/tests/Cadence.IntegrationTests" && echo "$work/none")"

new_gxp_tree() {
  local root="$work/$1"
  mkdir -p "$root/TestResults/gxp"
  if [ "${2:-}" != "" ]; then
    printf '%s\n' "$2" > "$root/TestResults/gxp/gxp_integration.trx"
  fi
  if [ "${3:-}" != "" ]; then
    printf '%s\n' "$3" > "$root/TestResults/gxp/gxp_e2e.trx"
  fi
  if [ "${4:-}" != "" ]; then
    printf '%s\n' "$4" > "$root/TestResults/gxp/kernel.database.txt"
  fi
  if [ "${5:-}" != "" ]; then
    printf '%s\n' "$5" > "$root/TestResults/gxp/gxp_unit.trx"
  fi
  echo "$root"
}

gxp_integration_pass='<TestRun><TestDefinitions><UnitTest id="1"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Sample" name="Passes" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="1" testName="Sample.Passes" outcome="Passed" /></Results></TestRun>'
gxp_integration_optin='<TestRun><TestDefinitions><UnitTest id="1"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Sample" name="Passes" /></UnitTest><UnitTest id="2"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Cadence.IntegrationTests.Kernel.LineageBenchmarkPostgresTests" name="BenchmarkTenMillionEdgesTraceLatency" /></UnitTest><UnitTest id="3"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests" name="BrowserCorrectionReachesTheDatabaseWithItsReason" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="1" testName="Sample.Passes" outcome="Passed" /><UnitTestResult testId="2" testName="Benchmark_TenMillionEdges_TraceLatency" outcome="NotExecuted" /><UnitTestResult testId="3" testName="BrowserCorrection_ReachesTheDatabaseWithItsReason" outcome="NotExecuted" /></Results></TestRun>'
gxp_integration_skip='<TestRun><TestDefinitions><UnitTest id="1"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Sample" name="SkippedDb" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="1" testName="Sample.SkippedDb" outcome="NotExecuted" /></Results></TestRun>'
gxp_integration_single_line_mixed='<TestRun><TestDefinitions><UnitTest id="1"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Cadence.IntegrationTests.Kernel.LineageBenchmarkPostgresTests" name="BenchmarkTenMillionEdgesTraceLatency" /></UnitTest><UnitTest id="2"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests" name="BrowserCorrectionReachesTheDatabaseWithItsReason" /></UnitTest><UnitTest id="3"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Sample" name="SkippedDb" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="1" testName="Benchmark_TenMillionEdges_TraceLatency" outcome="NotExecuted"><Output><StdOut>Opt-in benchmark skipped</StdOut></Output></UnitTestResult><UnitTestResult testId="2" testName="BrowserCorrection_ReachesTheDatabaseWithItsReason" outcome="NotExecuted"><Output><StdOut>Opt-in browser test skipped</StdOut></Output></UnitTestResult><UnitTestResult testId="3" testName="Sample.SkippedDb" outcome="NotExecuted"><Output><StdOut>CADENCE_TEST_POSTGRES is unset</StdOut></Output></UnitTestResult></Results></TestRun>'
gxp_e2e_pass='<TestRun><TestDefinitions><UnitTest id="2"><TestMethod codeBase="/bin/Cadence.E2ETests.dll" className="Sample" name="Flow" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="2" testName="Sample.Flow" outcome="Passed" /></Results></TestRun>'
gxp_unit_skip='<TestRun><TestDefinitions><UnitTest id="3"><TestMethod codeBase="/bin/Cadence.UnitTests.dll" className="Sample" name="UnitSkip" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="3" testName="Sample.UnitSkip" outcome="NotExecuted" /></Results></TestRun>'
gxp_db_txt='fixture=kernel
server_version=18.1'

gxp_browser_pass='<TestRun><TestDefinitions><UnitTest id="9"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests" name="BrowserCorrectionReachesTheDatabaseWithItsReason" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="9" testName="BrowserCorrection_ReachesTheDatabaseWithItsReason" outcome="Passed" /></Results></TestRun>'
gxp_browser_fail='<TestRun><TestDefinitions><UnitTest id="9"><TestMethod codeBase="/bin/Cadence.IntegrationTests.dll" className="Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests" name="BrowserCorrectionReachesTheDatabaseWithItsReason" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="9" testName="BrowserCorrection_ReachesTheDatabaseWithItsReason" outcome="Failed" /></Results></TestRun>'

expect_snapshot() {
  local name="$1" want="$2" root="$3" got=0
  CADENCE_GXP_SNAPSHOT=1 bash "$gate" "$root" >/dev/null 2>&1 || got=$?
  if [ "$got" -ne "$want" ]; then
    echo "FAIL: $name: exit $got, expected $want"
    failures=$((failures + 1))
  else
    echo "ok: $name"
  fi
}

expect "TestResults/gxp passes when database TRX and *.database.txt exist (ignoring unit skips)" 0 "$(new_gxp_tree gxp_pass "$gxp_integration_pass" "$gxp_e2e_pass" "$gxp_db_txt" "$gxp_unit_skip")"
expect "TestResults/gxp ignores opt-in benchmark and browser skips by DisplayName" 0 "$(new_gxp_tree gxp_optin "$gxp_integration_optin" "$gxp_e2e_pass" "$gxp_db_txt" "$gxp_unit_skip")"
expect "TestResults/gxp fails on a skipped database test" 1 "$(new_gxp_tree gxp_skip "$gxp_integration_skip" "$gxp_e2e_pass" "$gxp_db_txt")"
expect "TestResults/gxp fails on single-line xunit.v3 TRX combining opt-in skips with mandatory database skip" 1 "$(new_gxp_tree gxp_single_line_mixed "$gxp_integration_single_line_mixed" "$gxp_e2e_pass" "$gxp_db_txt")"
expect "TestResults/gxp fails when *.database.txt is missing" 1 "$(new_gxp_tree gxp_no_db "$gxp_integration_pass" "$gxp_e2e_pass" "")"
expect "TestResults/gxp fails when Cadence.E2ETests.dll TRX is missing" 1 "$(new_gxp_tree gxp_no_e2e "$gxp_integration_pass" "" "$gxp_db_txt")"

expect_snapshot "snapshot fails when browser correction TRX is NotExecuted and no browser shard TRX passed" 1 "$(new_gxp_tree gxp_snap_no_browser "$gxp_integration_optin" "$gxp_e2e_pass" "$gxp_db_txt")"
expect_snapshot "snapshot fails when browser correction TRX is completely absent" 1 "$(new_gxp_tree gxp_snap_absent_browser "$gxp_integration_pass" "$gxp_e2e_pass" "$gxp_db_txt")"

snap_with_browser="$(new_gxp_tree gxp_snap_pass "$gxp_integration_optin" "$gxp_e2e_pass" "$gxp_db_txt")"
mkdir -p "$snap_with_browser/TestResults/gxp/browser"
printf '%s\n' "$gxp_browser_pass" > "$snap_with_browser/TestResults/gxp/browser/gxp_browser.trx"
expect_snapshot "snapshot passes when browser shard TRX records Passed alongside dotnet shard NotExecuted" 0 "$snap_with_browser"

snap_with_failed_browser="$(new_gxp_tree gxp_snap_fail "$gxp_integration_optin" "$gxp_e2e_pass" "$gxp_db_txt")"
mkdir -p "$snap_with_failed_browser/TestResults/gxp/browser"
printf '%s\n' "$gxp_browser_fail" > "$snap_with_failed_browser/TestResults/gxp/browser/gxp_browser.trx"
expect_snapshot "snapshot fails when browser shard TRX records Failed" 1 "$snap_with_failed_browser"

if [ "$failures" -ne 0 ]; then
  echo "$failures gate case(s) failed"
  exit 1
fi
echo "All database-test gate cases behaved as expected."
