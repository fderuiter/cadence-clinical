# ADR-2026-07-24-continuous-fmea-gxp-aligned-exemption-ledger: Continuous FMEA GxP-Aligned Exemption Ledger

## Status
Accepted

## Context
To maintain absolute compliance with FDA 21 CFR Part 11 and EU Annex 11 within clinical trial software systems, any security exemption must be structured, documented, and risk-assessed. Previously, developers bypassed automated dependency vulnerability scans using undocumented, inline configuration flags. This created compliance gaps and risks GxP alignment.

## Decision
We introduce a centralized compliance ledger file at `docs/SDLC/vulnerability_exclusions_ledger.json` and a Python script at `scripts/validate_vulnerabilities.py` to:
1. Scan for inline configuration flags (`--ignore-vuln` or `--ignore-vulnerability`) in GitHub workflow and script directories. If found, automatically fail the build to block undocumented bypasses.
2. Load and validate the exclusions ledger. Ensure all fields exist, score ranges are between 1 and 5, and Risk Priority Number (RPN) is calculated correctly as $RPN = Severity \times Occurrence \times Detectability$.
3. Run `pip-audit --format json` and map active vulnerabilities against ledger entries.
4. Pass the build only if active vulnerabilities have validated exemptions in the ledger with an RPN under 20. Fail/block automatic progression if RPN >= 20.
5. Export a detailed JSON summary file so that PR comments dynamically display the exemption checklist status as a markdown table.

## Alternatives Considered
### Option 1: Ad-hoc `--ignore-vuln` inline flags in GitHub workflows
* **Overview:** Bypassing security checks directly in workflow files.
- ❌ Non-auditable and non-compliant with GxP validation.
- ❌ Risks deploying high-priority vulnerabilities.

### Option 2: Centralized JSON Exemption Ledger with FMEA Calculation
* **Overview:** The selected option. All exclusions are centralized and risk-assessed.

## Trade-offs
### Pros
- ✅ Centralizes and formalizes security exemptions.
- ✅ Blocks high-risk vulnerabilities (RPN >= 20) from automatic progression.
- ✅ Autogenerates structured audit trail summaries directly in Pull Request comments.

### Cons
- ❌ Slightly higher overhead for developers to register and document exemptions.

This decision implements requirements under Trace-1.
