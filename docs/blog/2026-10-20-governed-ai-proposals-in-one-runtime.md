---
title: "Governed AI in Clinical Research: Proposals, Grounding, and Human Attestation"
date: "2026-10-20"
authors:
  - "@fderuiter"
summary: "How ADR-2630 (K037) embeds AI agents across clinical workflows through one governed runtime where every output is a citation-verified kernel Proposal awaiting human signature."
tags:
  - "ai"
  - "kernel"
  - "governance"
  - "gxp"
related:
  - text: "ADR-2630: AI Agents Act Through Proposals in One Governed Runtime"
    link: "../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md"
  - text: "ADR-2632: Agents Act for One Person and Approved Proposals Execute as Their Approver"
    link: "../adr/2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md"
  - text: "ADR-2633: Agent Tools, Requester Grants, and Citation Verification"
    link: "../adr/2026-10-20-agent-tools-read-as-the-requester-and-every-claim-cites-what-was-read.md"
  - text: "AI Agents Catalogue & Autonomy Ladder"
    link: "../ai/AI_AGENTS.md"
draft: false
prev: false
next: false
---

# Governed AI in Clinical Research: Proposals, Grounding, and Human Attestation

Most AI features in enterprise healthcare fall into one of two traps: a detached chat sidebar whose unattributed text users paste into regulated forms, or ad-hoc model calls wired inside individual microservices with inconsistent prompts, privacy filters, and audit trails. Neither satisfies 21 CFR Part 11, ICH E6(R3), or the principle that accountable clinicians and trial staff—never automated models—decide clinical truth.

In [ADR-2630](../adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md) (kernel decision **K037**), we established a single governed agent runtime (`Cadence.Modules.Intelligence`) whose sole path to changing state is a kernel `Proposal`.

## Grounded or Silent (`K-AI-4` to `K-AI-8`)

Rather than scattering prompt logic across product services, modules contribute typed read tools and task definitions while the runtime and kernel enforce five invariants:

- **Grounded or silent (`K-AI-4`):** Every claim or proposed value must cite the kernel `NodeRef`s read during that run. A deterministic citation verifier strips any uncited value before a `Proposal` can be recorded, preventing fabricated measurements.
- **Qualified task configurations (`K-AI-5`):** The unit of validation is not a raw model name; it is a content-hashed **task configuration** binding the model, prompt, tool allow-list, output schema, FDA credibility-assessment context of use, and evaluation set. Any change requires requalification.
- **Requester-scoped authority (`K-AI-6`):** An agent acts on behalf of one authenticated person and reads exclusively through that person's bitemporal grants ([ADR-2633](../adr/2026-10-20-agent-tools-read-as-the-requester-and-every-claim-cites-what-was-read.md)). IdentityVault direct identifiers never reach a model port.
- **Bounded autonomy (`K-AI-7`):** A four-level autonomy ladder (`0 Inform`, `1 Draft`, `2 Approve-to-execute`, `3 Standing grant`) caps what each configuration may do. No standing grant may ever touch observations, eligibility, causality, coding approval, signatures, or unblinding.
- **Graceful independence (`K-AI-8`):** Every clinical study workflow functions identically when AI budgets pause or a tenant switches AI off.

## Dual Attribution and Human Execution

Approving a `Proposal` does not silently mutate records in the background. Under [ADR-2632](../adr/2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md), a reviewer inspects the proposed diff and its cited sources inside the Clinical Canvas, signs a Part 11 attestation bound to the proposal's content hash, and dispatches the resulting commands under their own verified identity referencing `CommandMetadata.Proposal` (`K-AI-2`). Every test runs in CI against a deterministic fake `IChatClient` provider, keeping verification fast, offline, and reproducible.
