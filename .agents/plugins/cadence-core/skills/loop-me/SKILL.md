---
name: loop-me
description: "Grill the user about specifications for workflows and clinical features within this workspace. Use when scoping a workflow or defining execution logic."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Workflow Looper

Executes a focused grilling session specifically tailored to scoping and validating multi-step clinical workflows (e.g. subject re-consent gating, SDV verification, query discrepancy lifecycle, site reimbursement triggers).

Calls `grill-with-docs` to map out the complete workflow state machine, pre-conditions, error transitions, and 21 CFR Part 11 audit trails.
