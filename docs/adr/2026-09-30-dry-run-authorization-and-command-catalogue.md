# ADR-2483: Dry-Run Authorization and the Command Catalogue

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Clinical Canvas command palette and the Act verb (Canvas doc PX-4) list the commands an actor can run on the
selected object. They show the ones the actor may not run as disabled, with an Explain (PX-7). That needs the
answer to "could I?" without executing anything. Issue #4847 (decision D8) records the gaps:

- The kernel only authorizes inside command execution (spec §5.1). The only way to learn whether a command would
  be permitted is to send it.
- `IAuthorizer.AuthorizeAsync(actor, command)` takes a whole command. A control on screen has a capability and an
  object, not a command payload.
- A dry-run check that used its own logic would drift from real decisions. A button enabled by the dry run would
  then fail when pressed, or stay disabled when it would have worked.
- Every real decision is audited in the tenant hash chain (K-AUTH-2). Rendering a screen may ask dozens of
  questions, and chaining each one would bury the audit trail in reads.

## 2. Decision Drivers & Constraints

- **One decision procedure.** A dry run and a real command must be answered by the same code, so they cannot
  disagree.
- **K-AUTH-1.** Authorization stays in the kernel.
- **K-AUTH-2 and 21 CFR Part 11.** Real decisions stay in the audit chain. A dry run needs a record, but not a
  record of a change to data.
- **No oracle.** A dry run must not let one person learn another's authority.
- **Requirements:** PRD-SYS-001 (audit trail): this ADR sets which authorization events enter the audit trail and
  which go to the access log.

## 3. Options Considered

### Option 1: Let the web app decide from roles it already knows

- **Cons:**
  - ❌ Authority is computed from evidence (§9.3), which the client does not have. The answers would drift.

### Option 2: A separate "permissions" read model

- **Cons:**
  - ❌ A second implementation of the policy, kept in sync by hand.

### Option 3: Authorization as a question to one evaluator, asked by the pipeline and by a probe (selected)

- **Overview:** the evaluator answers `AuthorizationRequest(actor, tenant, capability, resource, at, known_at)`.
  The command pipeline turns a command into that request through a catalogue of declared capabilities. The probe
  asks the same question directly.
- **Pros:**
  - ✅ The two paths cannot diverge: the evaluator is never told which one is asking.
  - ✅ The catalogue that turns commands into requests also lists the commands offered on an object.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** it gives the Canvas an exact answer with no second policy, and keeps the audit chain for
  changes.

### 4.1 Declared capabilities

- Every kernel command declares `[KernelCapability(token, verbClass, Resource = …)]`. The token is a
  `resource.action` capability (§9.3). The verb class is `Read | Propose | Execute | Approve`. `Resource` names the
  command property holding the kernel identifier it is authorized against. For a command that creates an object,
  that is the scope it is created in (the tenant for `RegisterOrganization`, the study for
  `DeclareStudyStandards`).
- `KernelCommandCatalog` reads the declarations once. A test fails when a kernel command has none. At runtime, a
  command without a declaration, or with an empty resource identifier, is denied before the evaluator is asked
  (fail closed).
- Current declarations: `tenant.provision`, `organization.register`, `code_system.register`,
  `code_system.import_version`, `concept.define`, `concept.revise`, `concept.deprecate`, `concept.map`,
  `concept_mapping.review`, `standard.register`, `standard.approve`, `standard.deprecate`, `library_item.draft`,
  `library_item.approve`, `library_item.deprecate`, `study.declare_standards`, `protocol.draft`,
  `protocol.revise_draft`, `protocol.submit_for_review`, `protocol.return_to_draft`, `protocol.approve`,
  `protocol.withdraw`, `protocol.amend` (on the approved parent) and `attestation.sign` (not offered in the
  catalogue; signing is part of the flow that needs it).

### 4.2 One path

```
command  → pipeline → IAuthorizer ─┐
                                   ├→ KernelAuthorization → AuthorizationRequest → IAuthorizationEvaluator
CanI(capability, resource) → probe ┘
```

- `KernelAuthorization` implements both `IAuthorizer` and `IAuthorizationProbe`. Both methods build the same
  `AuthorizationRequest` with the same clock, and call one private decision method.
- `AuthorizationRequest` has no "dry run" flag, so the evaluator cannot answer the two differently.
- The evaluator (#4781) computes the decision from Grants, Delegations and Qualifications, with every condition and
  its evidence (ADR-2482). Until #4781 lands, hosts supply their own evaluator.
- `IAuthorizationProbe.CanIAsync(tenant, capability, resource)` returns the `AuthorizationDecision`.
  `GetCommandsAsync(tenant, resource)` returns the command catalogue for that object: every command offered on
  its kind, each with its dry-run decision and explanation.

### 4.3 Audit tier

| Tier           | What                                                                                                                                              | Where                                                                                          | Why                                                                             |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| 1: audit chain | Real decisions: a permit is part of the command's audit entry, and a denial is its own audit entry and `DecisionDenied` event with its conditions | Tenant hash chain (§5.3)                                                                       | K-AUTH-2; 21 CFR 11.10(e) covers actions on records, including refused attempts |
| 2: access log  | Dry runs: tenant, actor, capability, resource, outcome, unmet condition codes                                                                     | Structured log event `2483 AuthorizationDryRun` (Information), retained with the security logs | Shows who probed what, so repeated probing is visible to security monitoring    |

A dry run satisfies Part 11 at tier 2 for these reasons:

- **No record is created, changed or deleted,** so 11.10(e) does not require an audit trail entry for it.
- **The real act is still recorded.** A person who acts on the answer sends the command, and that decision is
  audited at tier 1 whatever the dry run said.
- **Access control is enforced, not bypassed (11.10(d), (g)).** The probe answers only for the authenticated
  caller (`ICurrentActor`). With no accountable actor it denies (K-TX-5). It cannot ask about anyone else.
- **Nothing is revealed that the actor could not already see.** The answer is their own authority on an object
  they named. Evidence they may not read is masked under D1 (#4840) once read authorization exists (#4781).

### 4.4 Out of scope

- A kernel HTTP endpoint for `CanI` and the catalogue waits for tenant resolution and read authorization in the
  kernel API host (#4780, #4781), like the NodeRef endpoints (ADR-2481 §4.5). Until then applications call the
  probe in-process.
- Read capabilities (`identity.reveal.person`) are asked through `CanIAsync` directly; they have no command.

## 5. Consequences & Trade-offs

- **Positive Impact:** controls render enabled, or disabled with a real explanation, from the same decision the
  command would get. Every kernel command's capability is declared and tested.
- **Negative Impact / Technical Debt:** the catalogue asks one question per command offered on a kind, and each
  question is logged. Kinds have a handful of commands. A host can batch palette requests if that ever matters.
- **Mitigation Strategy:** the catalogue test makes a new command without a capability fail the build's tests, and
  the pipeline denies it at runtime.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`:
  - `Cadence.Kernel.Abstractions/Authorization`: `Capability`, `VerbClass`, `KernelCapabilityAttribute`. The
    declarations are on the commands in `Commands/` and `Standards/`.
  - `Cadence.Kernel.Application/Authorization`: `AuthorizationRequest`, `IAuthorizationEvaluator`,
    `IAuthorizationProbe`, `CommandAvailability`, `KernelCommandCatalog`, and `KernelAuthorization` (registered
    with `AddKernelAuthorization()`).
  - The command pipeline is unchanged; it keeps calling `IAuthorizer`.
- **Verification Plan:** `KernelAuthorizationTests`:
  - A command sent through the pipeline and a `CanI` for the same capability and resource put identical requests
    to one evaluator, and return equal decisions and explanations. Only the command opens a transaction or
    records a denial.
  - Dry runs are logged as event 2483 and write nothing to the chain. They are denied without an actor.
  - Every kernel command declares a unique capability with a registered resource kind.
  - The catalogue lists the commands for a kind, each with its decision.
  - Undeclared commands and empty resources are denied.
