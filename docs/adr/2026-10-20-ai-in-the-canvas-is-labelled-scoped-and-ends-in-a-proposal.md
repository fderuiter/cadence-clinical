# ADR-2635: AI in the Canvas Is Labelled, Scoped to the Selection and Ends in a Proposal

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #5549 (epic #5545); signed approval follows in #5552
- **Requirements:** PRD-SYS-051 (AI output needs human verification with a Part 11 signature and dual attribution),
  21 CFR 11.10(e), 11.50, 11.70
- **Builds on:** ADR-2630 §4.6 (the experience), ADR-2489 (the kernel Proposal), ADR-2471 (masking in place),
  ADR-2540 (the Canvas's kernel reads), ADR-2590 (the Clinical Canvas), UX-8 and UX-9 in
  [docs/ux/CLINICAL_CANVAS.md](../ux/CLINICAL_CANVAS.md)

---

## 1. Context & Problem Statement

ADR-2630 decides that agents have no screen of their own: they sit in the Canvas's normal panes, and their only way to
change anything is a kernel Proposal a person approves. The Canvas had none of the pieces that decision needs, and two
things pretended to be them:

- The kernel served no HTTP read of a proposal and no rejection, so no screen could show a real one.
- The protocol versions section had a "Generate Protocol Proposal" button backed by `GenerativeProtocolAuthor`, a
  fixed hypertension template presented as AI output with invented reasoning and citations. The inspector had
  "Approve Proposal" and "Reject" buttons that changed only browser state, and an "Inspect reasoning" dialog with
  hard-coded citations. These broke K-AI-4 (grounded or silent) and UX-8, and approval with no signature broke K033.

This ADR decides how AI appears in the Canvas and what the reviewer must do before deciding.

## 2. Decision Drivers & Constraints

- **UX-8:** AI output is labelled as AI output, links to what it reasoned over, and never executes past a person.
- **UX-9:** a control that can't be used says why.
- **K033 (ADR-2489):** approval is an attestation over the content hash by someone other than the proposer.
- **ADR-2630 §4.6:** Assist in the inspector, natural language in the palette, Proposals in Needs Attention (PX-10), a
  review screen with the citations and the signature; for high-risk configurations each cited source is opened before
  signing; the study works the same with AI switched off.
- **Honesty (§6.1 of the Canvas doc):** what no service serves yet is absent or disabled with its reason, never mocked.

## 3. Options Considered

### Option 1: An AI side panel or chat page

- ✅ One place to build.
- ❌ ADR-2630 rejects a separate AI surface; output would sit apart from the object it is about.

### Option 2: AI controls inside each section

- ✅ Close to the data.
- ❌ Each section would invent its own label, review and decision, which is how the fake approve buttons happened.

### Option 3: AI in the Canvas's existing panes, one review screen, one label (selected)

- ✅ Assist is an inspector verb on the selection; the palette takes a question; every proposal is reviewed on one
  screen whose rules live in one module (`src/ai/proposals.ts`).
- ❌ Until agents run (#5374, #5547) and signing is served (#5552), Ask and Approve are disabled with their reasons.

## 4. Decision Outcome

Option 3.

### 4.1 Kernel reads and rejection

The kernel API serves `GET /studies/{studyRef}/proposals?status=&limit=`, `GET /proposals/{proposalRef}` and
`POST /proposals/{proposalRef}/rejection`, behind the kernel tenant filter. A proposal, its plan and every affected
object the reader may not see stay in place with their mask (ADR-2471). Expired is read from the clock, as the kernel
already derives it. Rejection sends `RejectProposal` with the signed reason for change and the `Idempotency-Key`
header. Approval is not served: it needs an `Attest` from a fresh step-up signature, which #5552 adds with its durable
token store. `GenerativeProtocolAuthor` and its endpoint are deleted; an agent that drafts a protocol arrives through
the Intelligence module like any other.

### 4.2 The AI label and colour

`packages/ui/tokens.css` reserves `--semantic-color-ai`, `--semantic-color-ai-bg` and `--semantic-color-ai-border` for
agent output only. The `AiLabel` component shows the mark "AI" and words ("Proposed by an AI agent"), so it never
relies on colour, and names the model and, once recorded, the task configuration (K-AI-5, #5385). Every agent
proposal carries it in Needs Attention, Assist and the review screen.

### 4.3 Where AI appears

- **Needs Attention (PX-10)** on study home lists the study's open proposals, newest first, each opening the review.
- **Assist** is an inspector verb on a selected kernel node. It lists the study's proposals whose plan or affected
  objects name the node, and holds the question box. Until an agent runs in the study, Ask is disabled with its reason
  and Assist points at Act, whose dry runs answer "may I" without an agent.
- **The palette** offers a sentence of three or more words as "Ask AI". It opens Assist on the current selection (or
  study-wide with nothing selected) with the question carried in the URL (`ask=`). An answer can only read or propose.
- **The review screen** opens in the workspace at `?proposal=<NodeRef>`. It shows the intent, the proposer, the model,
  the task configuration (marked not recorded until #5385), the confidence, dates, content hash, simulation and
  escalation, the requested capabilities in words, every source with an Open control, and the decision.

### 4.4 Review rules

The screen offers approval only when nothing blocks it, and lists every blocker in words beside the disabled control:

| Blocker             | When                                                                                 |
| ------------------- | ------------------------------------------------------------------------------------ |
| not open            | The proposal is approved, rejected, withdrawn or expired                             |
| own proposal        | The reviewer proposed it (the kernel refuses self-approval too)                      |
| hidden sources      | The reviewer may not see an object it names, so can't review all of it               |
| unopened sources    | A high-risk proposal has a visible source the reviewer has not opened on this screen |
| signing unavailable | The deployment can't take the Part 11 signature yet (#5552)                          |

Until task configurations record a risk class (#5385), every agent proposal is reviewed as high risk, so the screen
never asks less than the strictest configuration would. A source with no kernel view of its kind counts as opened once
the reviewer has asked for it. Rejection needs a reason and is open to anyone the kernel grants `proposal.reject`. The
screen decides only what it asks of the reviewer; the kernel still enforces every rule itself.

### 4.5 AI switched off

A topbar control turns AI assistance off for this viewer, kept in their browser. Off removes the Assist verb and the
palette's Ask entry and changes nothing else. Needs Attention and the review screen stay, with their AI labels:
a proposal an agent already made is a kernel record a person must still decide. The tenant-wide switch that stops
agents running belongs to the Intelligence module (ADR-2630 §4.2, #5374).

## 5. Consequences & Trade-offs

- ✅ The Canvas shows real proposals from the kernel, labelled, with the reasoning sources one click away.
- ✅ The fabricated protocol author and the browser-only approval are gone.
- ✅ Turning AI off leaves every screen working.
- ❌ Approval stays disabled until #5552; Ask stays disabled until agents run (#5374, #5547).
- ❌ The review shows requested capabilities, not the exact commands and their diff, until K-AI-2 records them
  (#4783).

## 6. Implementation & Verification

- `tests/Cadence.UnitTests/Kernel/Api/KernelApiHostTests.cs`: proposals listed and read with hidden objects masked in
  place, invalid filters refused, a hidden study refused.
- `tests/Cadence.IntegrationTests/Kernel/KernelProposalHostPostgresTests.cs`: on PostgreSQL, an agent's proposal is
  listed, read and rejected by a granted reviewer and refused to a stranger; a rejection without a reason is refused;
  an expired proposal lists as expired and can't be rejected.
- `apps/web/tests/views/StudyCanvasProposals.spec.js` and `apps/web/tests/ai/proposals.spec.ts`: Needs Attention,
  the review rules and blockers, rejection, Assist, the palette's Ask, AI switched off, and axe checks on each pane.
