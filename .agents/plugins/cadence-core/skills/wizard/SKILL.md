---
name: wizard
description: "Generate an interactive CLI / bash wizard that walks a human through manual steps (cloud credential provisioning, SSO setup, external third-party dashboard navigation). Use when generating guided manual runbooks."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Interactive Wizard Generator

Generates a step-by-step interactive terminal script for operational tasks that only a human can execute (e.g. AWS IAM role provisioning, Keycloak client creation, external API key issuance) using Antigravity code generation tools.

## Antigravity Tool Execution Matrix

| Phase                | Antigravity Tool | Invocation Pattern                                                      | Objective                               |
| :------------------- | :--------------- | :---------------------------------------------------------------------- | :-------------------------------------- |
| **Wizard Script**    | `write_to_file`  | Target: `<appDataDir>/brain/<conversation-id>/scratch/<wizard_name>.sh` | Scaffold executable bash wizard script  |
| **Interactive Step** | `ask_question`   | `questions=[{"question": "Confirm Target Environment", ...}]`           | Interactively configure wizard defaults |

---

## Wizard Guidelines

1. **Interactive Prompts**: Use `read -p` to collect user inputs, validate inputs with regex, and confirm before performing actions.
2. **Idempotent Actions**: Ensure commands can be run repeatedly without duplicating configuration.
3. **Sensitive Secret Handling**: Never echo secrets to the terminal; use `read -s`.
4. **Validation Checks**: Verify connectivity or token validity at the conclusion of the wizard.
