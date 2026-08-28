---
name: grilling
description: "Core grilling procedure: relentless design-tree interview in frontier rounds until reaching a shared understanding. Use during design interviews and architectural stress-testing."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Grilling Core Procedure

Interview the user relentlessly until you reach a shared understanding using Antigravity search, interactive questioning (`ask_question`), and session artifacts. Map this as a **design tree**: every decision branches into the decisions that hang off it.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Fact Finding** | `grep_search` / `find_by_name` | Query files across `apps/` & `packages/` | Look up facts and constraints autonomously |
| **Frontier Decision** | `ask_question` | `questions=[{"question": "...", "options": [...]}]` | Prompt user with interactive multi-choice when appropriate |
| **Design Synthesis** | `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/grill_summary.md` | Emit structured design tree and settled decisions |

---

## The Grilling Loop

Work the tree in **rounds**. The **frontier** is every decision whose prerequisites are already settled: the questions you can ask _now_ without guessing at answers you haven't heard yet. Ask the whole frontier in one round: number each question and give your recommended answer. Then wait for the user's answers before the next round.

Each question should be formatted like so:

```
❓ **Q1** - **<question title>**: <question body, might be multiple paragraphs, including multiple choices>

➡️ <your recommended answer>
```
*(Or present via `ask_question` for direct modal selection).*

Each round the user answers reshapes the tree: settled decisions push the frontier outward and unblock questions that depended on them. Recompute the frontier and ask the next round. A question whose answer depends on another question still open in this round belongs to a _later_ round, not this one.

Finding _facts_ is your job, never the user's. When a frontier question needs a fact from the environment (filesystem, tools, etc.), use `grep_search` or `find_by_name`; don't ask the user for anything you could look up yourself. Don't block on it: ask the rest of the frontier now. The _decisions_ are the user's: put each to them and wait.

The session is done when the frontier is empty: every branch of the design tree visited, nothing left silently assumed. Do not act on it until the user confirms you have reached a shared understanding.

