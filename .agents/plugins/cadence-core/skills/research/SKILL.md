---
name: research
description: "Investigate technical, clinical standard (CDISC USDM/ODM), or regulatory (FDA 21 CFR Part 11, ICH GCP) questions using Antigravity research subagents and primary sources. Use when gathering background facts, inspecting codebases, or evaluating third-party libraries."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Research & Investigation

Delegates exploratory investigation against primary sources (codebase, CDISC USDM/ODM standards, FDA/EMA regulations, web documentation) to an Antigravity research subagent, compiling findings into a structured artifact.

## Antigravity Tool Execution Matrix

| Phase                  | Antigravity Tool                  | Invocation Pattern                                               | Objective                                                   |
| :--------------------- | :-------------------------------- | :--------------------------------------------------------------- | :---------------------------------------------------------- |
| **Web Research**       | `search_web` / `read_url_content` | `query="..."`, `Url="..."`                                       | Retrieve authoritative regulatory or clinical standard docs |
| **Codebase Search**    | `grep_search` / `find_by_name`    | `Query="..."`, `Pattern="..."`                                   | Locate existing implementations and domain patterns         |
| **Delegated Research** | `invoke_subagent`                 | `Subagents=[{"TypeName": "research", "Model": "flash"}]`         | Offload heavy search/read context to subagent               |
| **Artifact Report**    | `write_to_file`                   | Target: `<appDataDir>/brain/<conversation-id>/research_notes.md` | Emit structured research artifact with citations            |

---

## 1. Subagent Dispatch

Launch a dedicated `research` subagent to execute searches and file reading in a clean context:

```json
{
  "Subagents": [
    {
      "TypeName": "research",
      "Role": "Clinical & Codebase Researcher",
      "Model": "flash",
      "Workspace": "inherit",
      "Prompt": "Research the following question against primary sources:\n\nQuestion: <research-prompt>\n\nTarget areas:\n- Codebase files (use view_file, grep_search, find_by_name)\n- CDISC USDM / ODM standards & FDA guidance\n- Web documentation (use search_web, read_url_content)\n\nProvide exact citations, file references with line numbers, and structured trade-offs. Under 500 words."
    }
  ]
}
```

---

## 2. Compile Research Artifact

Synthesize the subagent's findings into `<appDataDir>/brain/<conversation-id>/research_notes.md` via `write_to_file`:

- **Executive Summary**: Clear 2-3 sentence answer to the core question.
- **Primary Source Evidence**: Citations with relative file links, API endpoints, or standard sections.
- **Architecture / Compliance Implications**: Impact on 21 CFR Part 11, USDM data flow, or system performance.
- **Recommendations**: Actionable technical options.

---

## 3. Present Concise Findings

Provide a concise overview in chat pointing to the generated artifact.
