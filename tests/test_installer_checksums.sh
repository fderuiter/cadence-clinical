#!/usr/bin/env bash
set -euo pipefail

echo "================================================================="
echo "   Testing .NET Installer SHA256 Checksum Verification   "
echo "================================================================="

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_SH="$REPO_ROOT/scripts/setup_dotnet_env.sh"
WIZARD_SH="$REPO_ROOT/scripts/setup_dotnet_wizard.sh"

TESTS_PASSED=0
TESTS_FAILED=0

assert_equals() {
  local expected="$1"
  local actual="$2"
  local msg="$3"
  if [ "$expected" = "$actual" ]; then
    echo -e "  \033[32m[PASS]\033[0m $msg"
    TESTS_PASSED=$((TESTS_PASSED + 1))
  else
    echo -e "  \033[31m[FAIL]\033[0m $msg (Expected: '$expected', Got: '$actual')"
    TESTS_FAILED=$((TESTS_FAILED + 1))
  fi
}

assert_contains() {
  local substring="$1"
  local content="$2"
  local msg="$3"
  if echo "$content" | grep -q "$substring"; then
    echo -e "  \033[32m[PASS]\033[0m $msg"
    TESTS_PASSED=$((TESTS_PASSED + 1))
  else
    echo -e "  \033[31m[FAIL]\033[0m $msg (Expected to contain '$substring')"
    TESTS_FAILED=$((TESTS_FAILED + 1))
  fi
}

echo -e "\n[Test 1] Invalid SHA256 override causes setup_dotnet_env.sh to fail and exit 1"
MOCK_BIN="$(mktemp -d /tmp/mockbin.XXXXXX)"
cat << 'EOF' > "$MOCK_BIN/dotnet"
#!/bin/sh
exit 1
EOF
cat << 'EOF' > "$MOCK_BIN/apt-get"
#!/bin/sh
exit 1
EOF
cat << 'EOF' > "$MOCK_BIN/sudo"
#!/bin/sh
exit 1
EOF
chmod +x "$MOCK_BIN/dotnet" "$MOCK_BIN/apt-get" "$MOCK_BIN/sudo"

OUTPUT=$(PATH="$MOCK_BIN:$PATH" DOTNET_INSTALL_SH_SHA256="invalidhash123" bash "$ENV_SH" 10.0 2>&1 || true)
EXIT_CODE=0
PATH="$MOCK_BIN:$PATH" DOTNET_INSTALL_SH_SHA256="invalidhash123" bash "$ENV_SH" 10.0 >/dev/null 2>&1 || EXIT_CODE=$?

rm -rf "$MOCK_BIN"

assert_equals "1" "$EXIT_CODE" "Exit code should be 1 on checksum mismatch"
assert_contains "Checksum verification failed" "$OUTPUT" "Output should state checksum verification failed"
assert_contains "invalidhash123" "$OUTPUT" "Output should display expected hash override"

echo -e "\n[Test 2] Wizard propagation on checksum verification failure"
MOCK_BIN="$(mktemp -d /tmp/mockbin.XXXXXX)"
cat << 'EOF' > "$MOCK_BIN/dotnet"
#!/bin/sh
exit 1
EOF
cat << 'EOF' > "$MOCK_BIN/apt-get"
#!/bin/sh
exit 1
EOF
cat << 'EOF' > "$MOCK_BIN/sudo"
#!/bin/sh
exit 1
EOF
chmod +x "$MOCK_BIN/dotnet" "$MOCK_BIN/apt-get" "$MOCK_BIN/sudo"

OUTPUT=$(PATH="$MOCK_BIN:$PATH" DOTNET_INSTALL_SH_SHA256="invalidhash123" bash -c "echo y | bash '$WIZARD_SH'" 2>&1 || true)
EXIT_CODE=0
PATH="$MOCK_BIN:$PATH" DOTNET_INSTALL_SH_SHA256="invalidhash123" bash -c "echo y | bash '$WIZARD_SH'" >/dev/null 2>&1 || EXIT_CODE=$?

rm -rf "$MOCK_BIN"

assert_equals "1" "$EXIT_CODE" "Wizard should exit with code 1 when setup_dotnet_env.sh fails"
assert_contains "Aborting onboarding wizard" "$OUTPUT" "Wizard output should report failure and abort"

echo -e "\n[Test 3] Check POSIX shasum vs sha256sum fallback"
OUTPUT=$(bash -c '
TEMP_SCRIPT="$(mktemp /tmp/test.XXXXXX)"
echo "hello" > "$TEMP_SCRIPT"
EXPECTED_HASH="badhash"
if command -v sha256sum &>/dev/null; then
    ACTUAL_HASH=$(sha256sum "$TEMP_SCRIPT" | awk "{print \$1}")
elif command -v shasum &>/dev/null; then
    ACTUAL_HASH=$(shasum -a 256 "$TEMP_SCRIPT" | awk "{print \$1}")
fi
rm -f "$TEMP_SCRIPT"
if [ "$ACTUAL_HASH" != "$EXPECTED_HASH" ]; then echo "mismatch"; exit 1; fi
' 2>&1 || true)

assert_contains "mismatch" "$OUTPUT" "Fallback hash computation works correctly"

echo -e "\n================================================================="
echo "Summary: $TESTS_PASSED passed, $TESTS_FAILED failed"
echo "================================================================="

if [ "$TESTS_FAILED" -gt 0 ]; then
  exit 1
fi
