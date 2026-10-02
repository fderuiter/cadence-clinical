---
name: handoff
description: "Compact the current conversation state, architectural decisions, and in-flight work into a structured handoff artifact for another agent or session. Use when concluding a long session, switching tasks, or handing off to another agent."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Session Handoff

Compacts the active conversation into an Antigravity handoff artifact so a future agent or session can resume with zero context loss using Antigravity state, task, and artifact tools.

## Antigravity Tool Execution Matrix

| Phase                 | Antigravity Tool | Invocation Pattern                                                                     | Objective                                               |
| :-------------------- | :--------------- | :------------------------------------------------------------------------------------- | :------------------------------------------------------ |
| **State Inspection**  | `run_command`    | `CommandLine="git status -s && git diff --stat"`                                       | Inspect uncommitted changes and branch state            |
| **Transcript Search** | `grep_search`    | Target: `<appDataDir>/brain/<conversation-id>/.system_generated/logs/transcript.jsonl` | Review earlier user instructions and tool calls         |
| **Handoff Artifact**  | `write_to_file`  | Target: `<appDataDir>/brain/<conversation-id>/handoff.md`                              | Emit structured handoff document with conversation link |

---

## 1. Gather Session State

- Current Conversation ID (from environment / metadata).
- Active git branch and status (`git status -s`, `git diff --stat`).
- Settled decisions vs. open questions.
- Unfinished work items or failing tests.

---

## 2. Generate Handoff Artifact

Write to `<appDataDir>/brain/<conversation-id>/handoff.md` with `ArtifactMetadata`:

```markdown
# Session Handoff: <Topic/Feature Name>

- **Originating Session**: [Conversation](conversation://<conversation-id>)
- **Timestamp**: YYYY-MM-DD HH:MM
- **Branch**: `<branch-name>`

## 1. Executive Summary

What was accomplished in this session.

## 2. Settled Decisions

- Decision 1 (rationale, ADR link if created)
- Decision 2

## 3. Active & In-Flight State

- Files modified: clickable [file.py:L1-L20](relative/path/to/file.py#L1-L20) links.
- Test outcomes: commands to run to inspect state.

## 4. Next Actions for Receiving Agent

1. Immediate next step with exact CLI command.
2. Open questions requiring user confirmation.
```

---

## 3. Present Final Response

Print a brief message in chat pointing the user to the `handoff.md` artifact.
