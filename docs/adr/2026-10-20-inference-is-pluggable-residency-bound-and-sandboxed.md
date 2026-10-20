# ADR-2636: Inference Is Pluggable, Residency-Bound and Sandboxed

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Issue:** #5550 (epic #5545)
- **Requirements:** PRD-SYS-051 (AI output needs human verification), 21 CFR 11.10(d) and (e); EU GMP Annex 11 §3
  (suppliers and service providers); HIPAA §164.502(b) (minimum necessary); GDPR Articles 5(1)(c), 28 and 44
- **Builds on:** [ADR-2630](2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md) §4.2, §4.4, §4.8
  (compute and inference layer) and §4.10 (HIPAA, GDPR and Annex 11 rows); ADR-2458 (IdentityVault residency)

---

## 1. Context & Problem Statement

ADR-2630 puts every model call behind one port, the `Microsoft.Extensions.AI` `IChatClient`, in the Intelligence
module, with Anthropic as the first provider and a deterministic fake for tests. It promises three things this ADR has
to make real:

- **Pluggable inference, not a walled garden.** A sponsor may require Claude through its own cloud contract (AWS,
  Google Cloud or Azure), or models it runs itself.
- **Tenant data stays in the deployment.** A tenant can be switched to local models only, and a tenant whose
  identifiers must stay in one region (`Tenant.Residency`, ADR-2458) must not have its clinical text sent to a model
  in another.
- **Sandboxed agent workers** with no network beyond the model port and the kernel read ports, and **no
  IdentityVault data in any prompt**.

Each provider is also a supplier of a computerised service. Annex 11 §3 and its 2025 draft require a documented
assessment of that supplier, and the GDPR requires a processing agreement and a lawful transfer basis. Nothing in the
codebase records either.

## 2. Decision Drivers & Constraints

- **K-AI-6, K-AI-8:** an agent acts for one person, and no workflow depends on an agent being available. A refused
  route is a normal outcome, never a blocked clinical task.
- **Fail closed:** when the router cannot prove a route is allowed, it refuses. It never fails over to a provider the
  tenant's policy does not allow, and never to a model the task configuration was not qualified on.
- **Protective controls are layered and honest:** free-text scrubbing reduces exposure; it is not de-identification
  and is never described as such (ADR-2630 §4.4).
- **Definitions as code (ADR-2630 §4.8):** supplier records and the provider catalogue are versioned files reviewed by
  pull request, so change control and the supplier file share one source.
- **CI never calls a model.** Every test runs on the fake provider or on a recording HTTP handler.
- **K-XS-2:** the module reads the tenant's residency through `Cadence.Kernel.Abstractions` only.

## 3. Options Considered

### Option 1: One provider per deployment, chosen by configuration

- ✅ Simplest; one API key, one supplier.
- ❌ One deployment cannot serve an EU tenant and a US tenant, or a local-only tenant beside a cloud one. A tenant's
  setting would not be enforced anywhere a test can see.

### Option 2: Each agent picks its own client

- ✅ Each agent can tune its provider.
- ❌ Every agent re-implements residency, scrubbing and supplier checks, which is the failure ADR-2630 option 2 rejects.

### Option 3: A provider catalogue behind one router that applies the tenant's inference policy (selected)

- ✅ Every run asks one router for a client. The router applies the tenant policy, the task configuration's qualified
  routes and the supplier status, and wraps the client in the outbound guard. One place to test, one place to audit.
- ✅ New providers are a catalogue entry and an adapter; agents do not change.
- ❌ The router is on every model call's path, so its checks must be cheap; they are in-memory except one cached
  residency read per tenant.

## 4. Decision Outcome

Option 3.

### 4.1 Provider catalogue

A provider implements `IInferenceProvider`: an id, a **locality** (`InDeployment` or `External`), the **residency
regions** its configured endpoint serves, its **supplier record**, and `CreateClient(modelId)` returning an
`IChatClient`. Providers are registered once and configured per deployment; an unconfigured provider is absent.

| Provider id         | Service                                                         | Operated by  | Client                                                        | Locality     | Residency comes from                                  |
| ------------------- | --------------------------------------------------------------- | ------------ | ------------------------------------------------------------- | ------------ | ----------------------------------------------------- |
| `anthropic`         | Claude API                                                      | Anthropic    | `Anthropic` SDK, `AsIChatClient` (Foundation, #5374)          | External     | None declared: serves tenants without a residency     |
| `anthropic-aws`     | Claude Platform on AWS                                          | Anthropic    | `Anthropic.Aws` `AnthropicAwsClient`                          | External     | The configured AWS region                             |
| `bedrock`           | Claude on Amazon Bedrock                                        | AWS          | `Anthropic.Bedrock` `AnthropicBedrockMantleClient`            | External     | The configured AWS region                             |
| `vertex`            | Claude on Google Cloud Vertex AI                                | Google       | `Anthropic.Vertex` `AnthropicVertexClient`                    | External     | The configured region; `global` is unrestricted only  |
| `foundry`           | Claude on Microsoft Foundry                                     | Microsoft    | `Anthropic.Foundry` `AnthropicFoundryClient`                  | External     | The configured Azure resource's region                |
| `openai-compatible` | A model the operator runs (vLLM, llama.cpp server, Ollama, TGI) | The operator | `Microsoft.Extensions.AI.OpenAI` with the endpoint's base URL | InDeployment | The deployment's own region                           |
| `fake`              | Deterministic test provider                                     | Cadence      | In-process (Foundation, #5374)                                | InDeployment | Any: sends nothing, so no residency or supplier check |

- **Residency is declared, not guessed.** Each configured provider names the residency codes it may serve (`EU`,
  `US`, …, the same two-to-eight upper-case codes as `Tenant.Residency`). Which cloud region counts as which
  jurisdiction is a legal fact the operator states in configuration and the supplier record, not a lookup table in
  code. A provider configured for a multi-region or global endpoint declares no residency and serves only tenants with
  none.
- **In-deployment means reachable only inside the deployment.** An `InDeployment` provider's endpoint must be a
  loopback or private address (RFC 1918, RFC 4193) or a cluster-internal host name (single label, `.svc`,
  `.cluster.local`, `.internal`); anything else fails startup. This is what makes "local-only" checkable rather than a
  label.
- **Default models are the latest Claude models** (`claude-opus-5-5`, with `claude-sonnet-5-5` and `claude-haiku-5-5`
  where a task configuration chooses them). Bedrock model ids take the `anthropic.` prefix; the provider adapter adds
  it, so task configurations name the first-party id.
- **The platform SDK packages are pre-1.0.** They are pinned in `Directory.Packages.props` like everything else and
  each adapter has contract tests against a recording HTTP handler, so a breaking release fails CI before it fails a
  run.

### 4.2 Tenant inference policy and routing

Every run gets its client from `IInferenceRouter.ResolveAsync(tenant, taskConfiguration, …)`, never from dependency
injection. The router decides in this order and refuses at the first failure with a typed reason:

1. **Kill switch and budget** (Foundation, ADR-2630 §4.2).
2. **Deployment mode.** `Intelligence:Inference:Mode = LocalOnly` (a sovereign deployment) makes every tenant
   local-only, whatever its own policy says.
3. **Tenant policy.** A tenant's inference policy is a module record: `Open`, `ResidencyBound` (the default) or
   `LocalOnly`, plus an optional list of allowed suppliers. It changes only through an audited module command,
   `intelligence.inference_policy.set`, declared with the Approve verb so no agent can change it (K-AI-1). With no
   record the tenant is `ResidencyBound`.
4. **Locality.** `LocalOnly` admits only `InDeployment` providers.
5. **Residency.** `ResidencyBound` admits a provider only when the tenant has no residency, or the provider declares
   the tenant's residency. The tenant's residency is read through a new kernel read port,
   `ITenantResidencyReader` (`Cadence.Kernel.Abstractions`), and cached per tenant.
6. **Supplier status.** Outside `Development`, a provider whose supplier record is not `Approved` is refused.
7. **Qualified routes.** The task configuration (ADR-2631) names the model and the provider routes it was qualified
   on. The router takes the first allowed route in the configuration's order. **Failover** (the provider is down, rate
   limited or refuses) goes only to the next route that passes steps 2 to 7; there is no failover to an unqualified
   model or a disallowed provider, and when no route remains the run ends with "AI unavailable", which K-AI-8 makes a
   normal outcome.

The decision (provider, supplier record version, model id, region, policy mode, refusals considered) is the run's
**inference route**, recorded in the interaction ledger with the run (21 CFR 11.10(e)).

### 4.3 The outbound guard

The router returns every client wrapped in an `OutboundGuardChatClient` (a `DelegatingChatClient`). Before any
request leaves the process it:

- **Refuses identity-layer content.** A message or content part carrying the identity-layer provenance mark (set by the
  tool layer, #5547, on anything derived from a node the kernel masked as `IdentityVault`) refuses the call; nothing is
  sent and the refusal is recorded. This is the third of four layers: the kernel already refuses every identity reveal
  to an `agent:` actor (reveals are `Execute` verbs, ADR-2489), kernel reads mask identity-layer nodes, the guard
  refuses marked content, and the scrubber below catches free text.
- **Scrubs free text** with `Cadence.Deid.DeidDetector`, replacing each detection with a typed placeholder
  (`[EMAIL]`, `[PHONE]`, …). The default scrub set is the direct identifiers (e-mail, telephone and fax, national ids,
  medical record and account numbers, health plan numbers, certificate and licence numbers, vehicle ids, URLs and
  IP or MAC addresses). Dates, ages and postcodes are **not** scrubbed by default, because the study's pseudonymous
  clinical data needs visit and onset dates to be reasoned over, and birth dates live only in the IdentityVault. A
  tenant policy can widen the set to a full profile (HIPAA Safe Harbor, GDPR, EU CTR).
- **Is one-way.** Placeholders are never mapped back into the response (the reversible air-gap tokens of ADR-2191 are
  not used for agents), so a scrubbed identifier cannot flow back into a Proposal.
- **Never alters tool results' citations.** Scrubbing changes free text only; node references the citation verifier
  needs are structured content and pass unchanged.
- **Records what it did** (counts by category, never the values) on the run's ledger entry.

The guard scrubs as a protective control. It is not evidence that a prompt is de-identified, and documentation says
so.

### 4.4 Sandboxed agent workers

An agent run executes inside an **agent sandbox**: a scope that holds the run's tenant, requester, task
configuration, budget and step limits, and the only two capabilities a run is given, the routed model client and the
tool set (kernel and module read ports, narrowed by the configuration's allow-list, K-AI-6). Inside it:

- **No other network.** Every `HttpClient` the host creates through `IHttpClientFactory` carries the sandbox egress
  handler. While a sandbox scope is active, the handler refuses any request except those sent by a model provider's
  own named client to its configured endpoint host. Provider clients additionally carry a fixed host allow-list, so a
  provider cannot be pointed elsewhere at run time.
- **No identity reveals.** The kernel authorizer refuses them to agent actors (above); the sandbox runs the agent as
  that actor.
- **No direct writes.** Tools are reads; the run's only output is a kernel Proposal sent by the runtime after the
  citation verifier (ADR-2630 §4.4).
- **Bounded.** Wall-clock, step and token limits from the configuration; exceeding one ends the run with its reason.
- **Code rules.** Architecture tests keep the agent and sandbox namespaces free of `System.Net.Sockets`,
  `new HttpClient`, `System.Diagnostics.Process`, file-system writes and reflection emit.

In-process enforcement is the floor. A deployment that wants an operating-system boundary runs the agent worker as its
own process or container from the same host image (`Intelligence:Worker:Role = AgentWorker`), whose network policy
admits only the configured model endpoints and the host's internal read API; the deployment guide gives the
Kubernetes `NetworkPolicy` and compose network for it. The in-process sandbox stays on in that mode too.

### 4.5 Supplier records

Each provider has a **supplier record**, a versioned file in the repository (`apps/intelligence/src/Inference/Suppliers/`)
and a page in `docs/ai/suppliers/`. It holds: the supplier and operating party, the service and endpoints, the
data-processing terms available (DPA, HIPAA BAA), whether customer data is used for training, retention and
zero-data-retention eligibility, the regions offered, the certifications the supplier states, the assessment date,
the assessor, the next review date, and a status: `Draft`, `Approved` or `Suspended`.

- The repository ships every record as `Draft`. **A sponsor's quality unit assesses and approves its suppliers**; the
  approval is a pull request (or a deployment overlay) changing the status, which is the change control Annex 11 asks
  for. Cadence does not claim a supplier's certifications on the sponsor's behalf.
- The router refuses a provider whose record is not `Approved` outside `Development` (§4.2 step 6), and a
  `Suspended` record everywhere.
- A record past its next review date raises an indicator for the quality unit; it does not stop routing, because a
  lapsed review is a finding, not a security failure.

### 4.6 Configuration

Configuration lives under `Intelligence:Inference` (environment variables `Intelligence__Inference__…`):
the deployment mode, and per provider its endpoint, region, declared residency codes and credentials. Credentials
come from the platform's own credential chain where one exists (AWS, Google ADC, Azure Entra ID) and otherwise from a
named secret; there is no default credential anywhere (ADR-2260). Each key is listed in
`docs/ENVIRONMENT_VARIABLES.md` in the same pull request that reads it.

## 5. Consequences & Trade-offs

- **Positive:** a sponsor can run Cadence's agents on Claude through its existing cloud contract or on its own models,
  per tenant; residency and local-only are enforced in one tested place; every run records exactly where it ran; the
  supplier file is part of the codebase's change control.
- **Negative:** a `ResidencyBound` tenant with no provider in its region gets no AI until one is configured; a local
  model must be qualified like any other before a configuration can route to it; the platform SDKs are pre-1.0.
- **Mitigation Strategy:** "AI unavailable" is a normal state (K-AI-8); the qualification evidence for a local model is
  the same evaluation run as for Claude (ADR-2634); SDK upgrades go through the contract tests.

## 6. Implementation & Verification

- **Slices** (issue #5550, after the Foundation's model port, #5374):
  1. this ADR and its first implementation, in one pull request: the provider abstraction on the Foundation's
     `IModelProvider` registry, the router behind `IRunChatClientFactory`, the residency read port, the outbound guard,
     the cloud and local providers, supplier records, the sandbox scope and egress handler, the route and scrub counts
     on the ledger entry, configuration and documentation. Until slice 2, every tenant has the default policy
     (`ResidencyBound`, direct-identifier scrubbing);
  2. the tenant policy record and its `Approve` command;
  3. the worker-process role with its deployment guide.
- **Verification Plan** (all on the fake provider or a recording HTTP handler; no test calls a model):
  - a `LocalOnly` tenant's run never reaches an external provider, including when the local provider fails;
  - a `ResidencyBound` tenant is never routed to a provider without its residency, and failover stays inside the
    allowed set;
  - an `InDeployment` provider with a public endpoint fails startup;
  - content carrying the identity-layer mark refuses the call before any request is sent;
  - free-text direct identifiers are replaced before the request leaves, and the ledger records counts only;
  - a request to any host other than the routed provider inside a sandbox is refused;
  - a provider whose supplier record is not `Approved` is refused outside Development;
  - the policy command is refused to an agent actor.
