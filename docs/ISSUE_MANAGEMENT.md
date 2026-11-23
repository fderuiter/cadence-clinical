# Issue management

This is the canonical workflow for Cadence's GitHub backlog. The
[project plan](PROJECT_PLAN.md) owns delivery scope;
[goals and priorities](GOALS_AND_PRIORITIES.md) owns product outcomes. Feedback,
questions, and early ideas belong in
[Discussions](https://github.com/fderuiter/cadence-clinical/discussions).
Development remains maintainer-led; an issue does not invite unsolicited
implementation PRs. Report vulnerabilities privately through
[SECURITY.md](../SECURITY.md).

## Vision and boundaries

Cadence is a pre-deployment modular monolith in one repository. Prove an
approved protocol through database lock using one synthetic medical-device
study, with low-friction entry, authoritative checks, discrepancy resolution,
versioned study build, cross-module handoffs, and inspectable evidence. Sequence
PostgreSQL verification, one real Capture visit, Blueprint's approved build
specification, and consent, safety, and evidence journeys. Tests that exist but
have not run are not qualification.

Billing and site finance, recruitment, downstream biostatistical analysis,
repository extraction, and production rollout before qualification are outside
the current delivery scope. Preserve useful constraints in the
[insights archive](BACKLOG_INSIGHTS_ARCHIVE.md), rather than keeping speculative
tickets open.

## Metadata

The personal repository uses type labels because native organization Issue Types
are unavailable. Horizon labels own planning horizons because no active linked
Projects v2 board was verified. Use native sub-issues and dependencies for
relationships. If a Project or native Issue Types later owns an axis, migrate
that axis once and remove its duplicate labels from active issues and forms.

Each triaged open issue has exactly one type, one priority, one horizon, and one
primary area. Now and Next have one size. Later epics have no size. An
unresolved needs-info item may omit priority when impact cannot yet be
established. Use at most one temporary status; otherwise omit status. New intake
starts with its form's type and status:needs-triage, then receives the remaining
metadata in triage.

There are 25 canonical issue labels. PR automation and historical labels are
retained separately; they are not alternatives for new issue triage. Do not
delete old labels while a live form, automation, or historical assignment still
requires them.

| Label                  | Meaning and application                                                          |
| ---------------------- | -------------------------------------------------------------------------------- |
| `type:bug`             | Verified behavior differs from the expected behavior.                            |
| `type:feature`         | Adds or extends a supported user capability.                                     |
| `type:chore`           | Tooling, verification, maintenance, or internal engineering work.                |
| `type:docs`            | Documentation, decision records, or written guidance.                            |
| `type:security`        | Security, privacy, or clinical trust-boundary remediation.                       |
| `priority:p0-critical` | Confirmed hard release blocker, outage, data loss, or active security issue.     |
| `priority:p1-high`     | Core delivery milestone or materially broken workflow.                           |
| `priority:p2-medium`   | Aligned improvement, ordinary defect, or technical debt.                         |
| `priority:p3-low`      | Small polish or low-impact cleanup.                                              |
| `horizon:now`          | Immediate ready work. Maximum 10 open issues.                                    |
| `horizon:next`         | Bounded upcoming work. Maximum 15 open issues.                                   |
| `horizon:later`        | Directional vision-aligned epic with reviewable milestones.                      |
| `status:needs-triage`  | New intake awaiting classification and a disposition.                            |
| `status:needs-info`    | Focused clarification or an owner decision is needed.                            |
| `status:blocked`       | A named dependency prevents the scoped work from advancing.                      |
| `status:ready`         | The issue meets the Definition of Ready.                                         |
| `area:kernel`          | Kernel contracts, authority, clinical transactions, rules, or module boundaries. |
| `area:capture`         | Clinical Canvas, staff eCRF, data entry, query, SDV, or locking.                 |
| `area:study-design`    | Blueprint, USDM, protocol effectivity, schedule, or study-build outputs.         |
| `area:clinical-ops`    | Trial modules, consent, allocation, safety, evidence, or quality oversight.      |
| `area:engineering`     | CLI, verification workflows, test tooling, documentation, or contributor setup.  |
| `size:xs`              | A focused change of less than two hours.                                         |
| `size:s`               | A small change of less than one working day.                                     |
| `size:m`               | A bounded change of one to three working days.                                   |
| `size:l`               | A change of three to five working days. Split larger increments.                 |

### Priority decisions

| Priority             | Objective criterion                                                                                                                                                           |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| priority:p0-critical | Confirmed data loss, active security issue, outage, or hard qualification/release blocker with no acceptable workaround. Pre-deployment does not imply a production incident. |
| priority:p1-high     | Core vision milestone or materially broken clinical workflow. Sequence dependencies before dependent implementation.                                                          |
| priority:p2-medium   | Aligned improvement, ordinary defect with a workaround, or meaningful technical debt.                                                                                         |
| priority:p3-low      | Minor polish or low-impact cleanup.                                                                                                                                           |

Priority describes impact, while horizon describes when a bounded outcome can be
advanced. A security blocker may be Next pending its required evidence or design
decision. Record that dependency.

## Horizons and readiness

| Horizon | Admission and limit                                                                                                                           |
| ------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Now     | Immediate 1–2 iterations; at most 10 open issues, initially 5. Every item meets the Definition of Ready.                                      |
| Next    | Upcoming 2–3 iterations; at most 15 open issues. State context, bounded scope and non-goals, rough size, and open questions.                  |
| Later   | Directional, vision-aligned epics only. Record alignment, desired outcome, and high-level milestones; do not file their speculative subtasks. |

There is no Icebox. Move uncommitted ideas to Discussions or archive their
useful context and close with not planned. Promotion requires capacity in the
destination horizon. These caps apply to triaged work; review new intake weekly
so it does not become a second backlog.

### Definition of Ready

Now issues and status:ready issues must be independent, negotiable, valuable,
estimable, small, and testable. Their bodies include:

- [ ] Vision alignment and context, with source issue or discussion links.
- [ ] A bounded expected outcome, scope, and explicit non-goals.
- [ ] Binary, testable acceptance criteria written as checkboxes.
- [ ] Verified current code paths or symbols, with the inspected revision when
      useful.
- [ ] Unresolved dependencies and risks, if any, linked natively.
- [ ] A verification plan using the relevant unit, integration, browser, or
      manual checks.

Keep Now work to at most five working days. Size estimates are relative guides,
not commitments. Split larger outcomes into independently useful vertical
slices; avoid arbitrary frontend/backend splits. A Later parent remains a
roadmap owner while a bounded child is promoted.

## Titles, provenance, and relationships

Use imperative or symptom-first titles without bracketed metadata. For a
substantial rewrite, preserve the untouched original body in a collapsed
Original Issue Context block. Leave original comments, repros, screenshots,
stack traces, and external links intact.

Before consolidating, read the source body and comments and check current code,
recent commits, and PRs. Transfer each unique constraint to a surviving issue or
a categorized archive entry, including exact source and comment links. Link both
directions before closure. Completed requires proof that every acceptance
criterion is satisfied on main; consolidation or deferral uses not planned. Use
duplicate only when the surviving acceptance is equivalent, not merely
thematically related.

Use native parent/sub-issue links for decomposition and native
blocked-by/blocking links for actual prerequisites. A parent is not
automatically a blocker. Do not introduce a cycle or close an unresolved
prerequisite merely because its scope moved into an epic. Transfer the scoped
gate and verify the replacement edge first. If an authenticated write surface
cannot change native edges, retain them and pause affected closures; keep their
mapped owners visible in the archive. When native features are unavailable in
GitHub itself, use explicit issue links with a short dependency statement.

## Maintainer cadence

Review intake weekly during active development: validate reality and vision,
deduplicate, choose metadata, and promote only within the caps. Use
status:blocked only with a named prerequisite. For status:needs-info, ask 1–3
focused questions and review after 14 days. Missing information merits a final
prompt or respectful disposition, not automatic closure based on age alone.

Review Later monthly for relevance and progress. Reuse or reopen an existing
scoped source when promoting a milestone; create a new issue only when no
existing source describes that deliverable. Check templates against live labels
whenever this schema changes. Repository file changes go through a dedicated
branch and PR; generated qualification records remain integration-owner outputs.
