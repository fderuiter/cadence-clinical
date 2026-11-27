#!/usr/bin/env bash
# Pins Playwright's Chromium browser process tree (including Chrome_ChildIOT /
# NetworkService) to a single allowed CPU core on Linux hosts.
#
# Chromium 156.0.8078.4 (bundled with @playwright/test 1.65.1) enables CHECK
# invariants in HttpNetworkTransaction (Net.NetworkTransaction.StreamCreationTime3.)
# that abort with SIGTRAP (int3/ud2) when microsecond cross-vCPU CLOCK_MONOTONIC
# skew occurs on virtualized multi-core runners, terminating NetworkService mid-load
# and failing in-flight module subresources with net::ERR_ABORTED.
set -euo pipefail

target_bin="${CADENCE_REAL_CHROMIUM_BIN:-}"
if [[ -z "${target_bin}" ]]; then
  echo "CADENCE_REAL_CHROMIUM_BIN must point to the Chromium executable." >&2
  exit 1
fi

if command -v taskset >/dev/null 2>&1; then
  affinity_output="$(taskset -cp "$$" 2>/dev/null || true)"
  affinity_list="$(printf '%s\n' "${affinity_output}" | sed -nE 's/.*:[[:space:]]*([0-9,-]+).*/\1/p')"
  allowed_cpus=()
  if [[ -n "${affinity_list}" ]]; then
    IFS=',' read -ra cpu_ranges <<< "${affinity_list}"
    for range in "${cpu_ranges[@]}"; do
      if [[ "${range}" =~ ^([0-9]+)-([0-9]+)$ ]]; then
        start_cpu="${BASH_REMATCH[1]}"
        end_cpu="${BASH_REMATCH[2]}"
        for ((c = start_cpu; c <= end_cpu; c++)); do
          allowed_cpus+=("${c}")
        done
      elif [[ "${range}" =~ ^[0-9]+$ ]]; then
        allowed_cpus+=("${range}")
      fi
    done
  fi
  worker_idx="${TEST_PARALLEL_INDEX:-0}"
  if [[ ! "${worker_idx}" =~ ^[0-9]+$ ]]; then
    worker_idx=0
  fi
  if (( ${#allowed_cpus[@]} > 0 )); then
    cpu_core="${allowed_cpus[$((worker_idx % ${#allowed_cpus[@]}))]}"
  else
    cpu_core=0
  fi
  if taskset -c "${cpu_core}" true >/dev/null 2>&1; then
    exec taskset -c "${cpu_core}" "${target_bin}" "$@"
  fi
fi

exec "${target_bin}" "$@"
