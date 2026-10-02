---
name: code-review
description: "Perform a parallel two-axis review of git changes against Cadence GxP standards and specification requirements using Antigravity subagents and artifacts. Use when reviewing a PR, branch diff, or uncommitted changes."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Two-Axis Code Review

Reviews changes along two distinct, decoupled axes to ensure full standards adherence and specification faithfulness without masking findings using Antigravity parallel subagents and session artifacts.

## Antigravity Tool Execution Matrix

| Phase                 | Antigravity Tool  | Invocation Pattern                                                                                           | Objective                                                      |
| :-------------------- | :---------------- | :----------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------- |
| **Diff Inspection**   | `run_command`     | `CommandLine="git diff <fixed-point>...HEAD"`, `WaitMsBeforeAsync=5000`                                      | Fetch exact unified diff range                                 |
| **Parallel Review**   | `invoke_subagent` | `Subagents=[{"Role": "Standards & GxP Auditor", "Model": "pro"}, {"Role": "Spec Verifier", "Model": "pro"}]` | Launch concurrent decoupled review subagents                   |
| **Inter-Agent Comms** | `send_message`    | `Recipient="<conversationId>"`, `Message="..."`                                                              | Communicate clarifying instructions to subagents               |
| **Remediation Gate**  | `ask_question`    | `questions=[{"question": "Select Remediation Approach for Critical Review Findings", ...}]`                  | Present user with interactive choices for high-severity issues |
| **Artifact Report**   | `write_to_file`   | Target: `<appDataDir>/brain/<conversation-id>/code_review.md`                                                | Emit side-by-side review report with alerts and file links     |

---

## Review Process

### 1. Identify the Fixed Point & Diff

Determine the target diff:

- For a branch or PR: `git diff <fixed-point>...HEAD`
- For working tree changes: `git diff HEAD`
- Check the commit list: `git log <fixed-point>..HEAD --oneline`
- Confirm the ref resolves (`git rev-parse <fixed-point>`) before launching subagents.

### 2. Identify Spec & Standards Sources

1. **Spec Source**:
   - Issue reference or PR description
   - Requirement IDs (`PRD-SYS-xxx`, `Trace-x`)
   - `docs/SDLC/01_Product_Requirements_Document_PRD.md` or `docs/SRS.md`
2. **Standards Source**:
   - `AGENTS.md` (root guidelines)
   - Fowler smell baseline (Mysterious Name, Duplicated Code, Feature Envy, Data Clumps, Primitive Obsession, Repeated Switches, Shotgun Surgery, Divergent Change, Speculative Generality, Message Chains, Middle Man, Refused Bequest).

### 3. Launch Parallel Subagents via `invoke_subagent`

Call `invoke_subagent` using the `pro` model tier for deep reasoning and GxP compliance checking:

```json
{
  "Subagents": [
    {
      "TypeName": "self",
      "Role": "Standards & GxP Auditor",
      "Model": "pro",
      "Workspace": "inherit",
      "Prompt": "Perform the Standards & GxP axis review on the diff between <fixed-point> and HEAD.\n\nDiff command: `git diff <fixed-point>...HEAD`\n\nStandards sources: AGENTS.md, GxP 21 CFR Part 11 requirements, Fowler code smells.\n\nStrictly check: E712 boolean filter `.is_(True/False)`, I001 alphabetical imports, PEP 695 generics, Part 11 audit fields, and Fowler smells.\n\nOutput your findings grouped by file with line references. Under 400 words."
    },
    {
      "TypeName": "self",
      "Role": "Spec & Requirement Verifier",
      "Model": "pro",
      "Workspace": "inherit",
      "Prompt": "Perform the Spec Compliance axis review on the diff between <fixed-point> and HEAD.\n\nDiff command: `git diff <fixed-point>...HEAD`\nSpec source: <spec-path-or-issue>\n\nReport:\n1. Missing or partial requirements.\n2. Scope creep (unrequested behavior).\n3. Misimplemented requirements with exact spec quotes.\n\nOutput your findings with line references. Under 400 words."
    }
  ]
}
```

Stop calling tools to await subagent completion. The Antigravity messaging system will automatically resume execution when both subagents finish.

### 4. Synthesize into Antigravity Artifact

Write the consolidated review to `<appDataDir>/brain/<conversation-id>/code_review.md` using `write_to_file` with `ArtifactMetadata`:

- Header with reviewed commit/diff range and spec source.
- `## Standards & GxP Compliance` section with file links and line numbers.
- `## Specification Alignment` section citing exact requirement IDs (`@req:PRD-SYS-xxx`).
- Summary alert box (`[!IMPORTANT]` / `[!TIP]`) with the single most critical finding per axis.

### 5. Interactive Remediation & Summary

If critical GxP or spec deviations are detected, prompt the user with `ask_question` to determine whether to auto-remediate with `implement` or branch into a refactoring ticket:

```json
{
  "questions": [
    {
      "question": "How would you like to address the critical review findings in <component>?",
      "options": [
        "(Recommended) Auto-remediate immediately via implement skill and sync GxP compliance",
        "Scaffold a dedicated refactoring ticket under docs/SDLC/ and defer non-blocking items"
      ],
      "is_multi_select": false
    }
  ]
}
```

In your final chat response, provide a high-level 2-sentence summary and point the user to the generated `code_review.md` artifact.
