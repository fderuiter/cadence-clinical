# Intelligence module

The Intelligence engine's runtime for AI agents ([ADR-2630](../../docs/adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md), #5374). It is a kernel module ([ADR-2600](../../docs/adr/index.md)) composed into the kernel host [`apps/kernel-api`](../kernel-api/README.md), not a service of its own; it replaces the retired `apps/ai_gateway` ([ADR-2610](../../docs/adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)).

**Status:** foundation. The module calls a model, records every call in an append-only ledger, and enforces per-tenant kill switches and token budgets. Agents run only on task configuration versions the tenant has qualified ([ADR-2631](../../docs/adr/2026-10-20-agent-task-configurations-are-versioned-and-qualified-per-tenant.md), #5385). Agents do not yet act on clinical records: when they do, they act only through kernel Proposals (K-AI-1..8). Qualification rests on stored evaluation runs, and production signals suspend a configuration ([ADR-2634](../../docs/adr/2026-10-20-ai-task-configurations-are-measured-by-versioned-evaluations-and-production-signals.md), #5548, #5574). The kernel side of the agent actor is built (`Actor.ActingFor`, requester-scoped grants, [ADR-2632](../../docs/adr/2026-10-20-agents-act-for-one-person-and-approved-proposals-execute-as-their-approver.md), #4783), but the module does not act as an agent actor yet; that comes with the agent tools, the citation verifier and Proposal-writing runs (#5547). Every call is routed by tenant policy, residency and supplier status, scrubbed of direct identifiers, and run inside an egress-locked sandbox ([ADR-2636](../../docs/adr/2026-10-20-inference-is-pluggable-residency-bound-and-sandboxed.md)).

## Project

- Project: `apps/intelligence/src/Cadence.Modules.Intelligence.csproj` (namespace `Cadence.Modules.Intelligence`). Of the kernel it references `Cadence.Kernel.Abstractions` and the module SDK `Cadence.Kernel.Modules` only (K-XS-2); `packages/deid` supplies the outbound scrubber.
- `IntelligenceModule`: `AddCadenceIntelligenceModule(configuration, environment, connectionString)` registers the module, its command store, migrations, tenant-scoped read context, handlers, providers and the `AgentRunner`; `AddAgentTask(TaskConfiguration)` registers a task version (several versions of a key may be registered; one version twice fails at startup); `MapIntelligenceModule()` maps the endpoints.
- `Domain/`: `TaskConfiguration` (key, version, provider, model or `tier:` alias, instructions template, output limit, autonomy level; tools, retrieval sources, output schema, pinned evaluation set, acceptance criteria, approver role and the FDA credibility assessment with its risk class; and a kernel `ContentHash` over all of them, K-AI-5), `TaskQualification` (one tenant's signed qualification of one version, or its suspension), `EvaluationRun` (a stored evaluation run record a qualification rests on, admitted only when `EvaluationRun.Problems` finds nothing), `PromptTemplate` (strict `{{name}}` rendering), `AgentRun` (one ledger entry with its own content hash), `AgentControl` (a kill switch and monthly token budget for all tasks, `*`, or one task key), `IntelligenceErrors`.
- `Application/`: the module commands `StartAgentRun`, `CompleteAgentRun` (capability `agent_run.record`, verb Propose), `SetAgentControl` (capability `agent_control.set`, verb Execute), `RecordEvaluationRun` (capability `evaluation_run.record`, verb Execute), `QualifyTaskConfiguration` (capability `task_configuration.qualify`, verb Approve, so people only) and `SuspendTaskConfiguration` (capability `task_configuration.suspend`, verb Execute) with their handlers and validators; the `task_configuration` attestation target check; the ports `ITaskConfigurationCatalog`, `IModelProvider`, `IModelProviderRegistry`, `IRunChatClientFactory`, `IAgentLedger`, `IEvaluationRuns`, `ITaskConfigurationSuspension`; and `AgentRunner`.
- `Infrastructure/Models/`: the model port is Microsoft.Extensions.AI `IChatClient`. The Anthropic provider uses the official `Anthropic` SDK and is configured only when an API key is set; `FakeChatClient` answers deterministically and is allowed only in `Development` and `Testing`, so CI never calls a real model.
- `Infrastructure/Persistence/`: `IntelligenceDbContext` (schema `intelligence`, tables `agent_runs`, `agent_controls`, `task_qualifications` and `evaluation_runs`, all under tenant Row-Level Security), its migrations, `EfAgentLedger` and `IntelligenceDatabase.GrantAppRoleSql`.
- `Tools/`: the governed agent tools ([ADR-2633](../../docs/adr/2026-10-20-agent-tools-read-as-the-requester-and-every-claim-cites-what-was-read.md), #5547). `AgentPrincipal` (an agent acting for one person in one tenant), requester-bound visibility, the tool catalogue, task allow-lists, sessions, the `untrusted_data` result envelope, and the seven read tools in `Tools/Kernel/` (`get_node`, `list_children`, `search_kernel`, `trace_lineage`, `explain`, `can_i`, `simulate`). `can_i` and `simulate` answer "not available" until the host provides `IAgentDryRun` and `IAgentSimulation` (ADR-2632). No code here can reach a command sender (architecture test).
- `Grounding/`: the citation verifier (K-AI-4), value fingerprints, the plan document and `AgentProposalWriter` (K-AI-5). The writer is registered separately with `AddIntelligenceProposalWriter()` once the host provides `IPlanArtifactStore` and `IAgentProposalGateway`.
- `Mcp/`: the MCP server for outside agents over the same tools, without `simulate`.
- `Evaluation/`: the measurement layer ([ADR-2634](../../docs/adr/2026-10-20-ai-task-configurations-are-measured-by-versioned-evaluations-and-production-signals.md), [guide](../../docs/ai/EVALUATION.md)): versioned evaluation sets, deterministic checks, the calibrated LLM judge, the `EvaluationHarness` and its hashed run records, improvement comparisons, and production signals per configuration version (`ConfigurationMonitor`, `ProductionSignalRecorder`). `AddIntelligenceEvaluation()` registers the harness.
- `Inference/` and `Sandbox/`: the inference layer of ADR-2636, registered by `AddIntelligenceInference` (see [Inference and data sovereignty](#inference-and-data-sovereignty)).
- `Intent/` and `Endpoints/IntentEndpoints.cs`: the rule-based command-palette intent router, moved from the AI gateway unchanged.

## Qualification

A configuration version runs in a tenant only after a person qualifies it there, and only while its content is the content qualified ([ADR-2631](../../docs/adr/2026-10-20-agent-task-configurations-are-versioned-and-qualified-per-tenant.md)):

1. The version must be complete: a pinned evaluation set, acceptance criteria, the credibility assessment (question of interest, context of use, model influence, decision consequence) and, from autonomy level Draft, an approver role.
2. The qualifier signs an `Approved` kernel attestation over target kind `task_configuration`, the id `TaskConfiguration.AttestationTargetId(key, version)` and the configuration's content hash.
3. The operator stores the evaluation run with `RecordEvaluationRun` (#5574). The server refuses a record that evaluated another version or content, another set, other acceptance criteria or another provider, marks a case passed although a check failed, or whose metrics and verdict are not what `EvaluationHarness.Measure` and `Decide` give from its case results. It keeps the record as canonical JSON with its kernel content hash; a rejected run is stored too.
4. `QualifyTaskConfiguration` names the stored run by id. It checks the hash, that the run evaluated exactly this version against the pinned set and was accepted, and that the signature is the caller's own, then records the qualification with its risk class and the run's id and content hash. The caller never states that a run passed.

`SuspendTaskConfiguration` (or `ITaskConfigurationSuspension`, for monitoring) takes a version out of use with a reason; only a new qualification restores it. A changed configuration is a new version and must be qualified on its own.

## How a run works

`AgentRunner.RunAsync` picks the newest version of the task the tenant has qualified on its current content (or refuses, saying why: not qualified, suspended, or changed since qualification), resolves its provider and model, refuses the run when a control is off or the tenant's monthly tokens are spent, asks `IRunChatClientFactory` for the run's client (refused with `M.Intelligence.Inference.Refused` when no allowed route is left), renders the instructions, and records `StartAgentRun` through the kernel command pipeline before it calls the model. The model call has a timeout (`Intelligence:RequestTimeoutSeconds`); success, failure and cancellation are all recorded with `CompleteAgentRun`, with token counts and cost, and raise `intelligence.AgentRunCompleted`. The start command re-checks the kill switch, the configuration's content and its qualification inside its transaction. Logs carry ids and counts only, never prompts or answers.

Every ledger entry holds the task version and configuration hash, the resolved model, the requester, the request and the answer, and where the call went (supplier, locality, tenant policy and residency) with the outbound guard's counts per category. It is written only through the pipeline (audit columns, idempotency, authorization) and the application role cannot delete it. Only the person who started a run can complete it or read it.

## Inference and data sovereignty

ADR-2636 decides where an agent's model call may go and what it may carry. Everything below is in `src/Inference`
and `src/Sandbox`, and `AddCadenceIntelligenceModule` wires it with `AddIntelligenceInference(configuration, environment)`. It replaces the default `IRunChatClientFactory`, which hands a run the provider's own client.

### Providers

| Id                  | Reaches                              | Locality     | Supplier record     |
| ------------------- | ------------------------------------ | ------------ | ------------------- |
| `anthropic-aws`     | Claude Platform on AWS               | External     | `anthropic-aws`     |
| `bedrock`           | Amazon Bedrock                       | External     | `aws-bedrock`       |
| `vertex`            | Google Vertex AI                     | External     | `google-vertex`     |
| `foundry`           | Microsoft Foundry                    | External     | `microsoft-foundry` |
| `openai-compatible` | a model server inside the deployment | InDeployment | `operator-hosted`   |

A provider is registered only when its configuration section is present ([configuration keys](../../docs/ENVIRONMENT_VARIABLES.md#intelligence-inference)).
Each one gets a named `IHttpClientFactory` client (`cadence-inference:<id>`) that refuses any host the provider did
not declare. Each one is also an `IModelProvider` in the registry, so a task configuration names it like `anthropic` or `fake`; model ids stay first-party in the ledger and price list. Another module adds a provider with `services.AddInferenceProvider(id, factory)`.

### Routing

`IInferenceRouter.ResolveAsync` builds the client for one agent run. The module's `anthropic` provider is routed as the external supplier `anthropic` and the `fake` provider as in-process, which needs no residency or supplier. It refuses, in this order:

1. every external provider when the deployment runs with `Intelligence:Inference:Mode = LocalOnly`;
2. the tenant's policy: `LocalOnly`, `ResidencyBound` (the default) or `Open`, with an optional list of allowed suppliers;
3. a provider that does not declare the tenant's residency (read through the kernel's `ITenantResidencyReader`);
4. a supplier that is not `Approved` outside Development, and a `Suspended` supplier everywhere;
5. models the task configuration did not qualify.

Failover happens only between the routes that are left. Every response carries the route it used under
`cadence.inference_route`.

### Outbound guard

Before anything leaves, `OutboundGuardChatClient` refuses identity-layer content (`IdentityLayerMark`) and binary or
file content it cannot inspect. It then scrubs direct identifiers one way with `packages/deid`. It does not scrub dates,
ages or postcodes by default, because the clinical reasoning needs them. The counts are recorded under
`cadence.outbound_scrub`.

### Sandbox

`AgentSandbox.Enter(tenant, runId, limits)` opens the scope for one run. Inside it, every `IHttpClientFactory` client
except the inference clients and `Intelligence:Sandbox:AllowedClients` is refused, and the run stops at its wall-clock,
model-call or token limit. `InferenceSandboxBoundaryTests` keeps sockets, hand-built `HttpClient`s, processes and file
access out of this code.

### Supplier records

`src/Inference/Suppliers/*.json` ship as `Draft`, with the Annex 11 assessment questions open. The sponsor's QA approves
a supplier in configuration (`Intelligence:Inference:Suppliers:<id>`), and an approval must name who assessed it, when,
and the next review date.

## HTTP surface

All routes need the gateway-signed caller and run in the caller's kernel tenant. The wire is snake_case.

| Route (`/api/v1/ai/…`)                              | Answers                                                                                                                                                                                                                                                                                                                                                                                  |
| --------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET runs`                                          | The caller's own runs, newest first, each with `content_hash_verified`.                                                                                                                                                                                                                                                                                                                  |
| `GET runs/{runId}`                                  | One of the caller's runs; another person's run is 404.                                                                                                                                                                                                                                                                                                                                   |
| `GET tasks`                                         | The registered task configuration versions with their hashes, risk classes and the tenant's qualifications.                                                                                                                                                                                                                                                                              |
| `POST evaluation-runs`                              | Stores an evaluation run record (`RecordEvaluationRun`); the body is the harness's run record as is (camelCase, the evaluation file format); needs `Idempotency-Key` and a signed change reason.                                                                                                                                                                                         |
| `GET evaluation-runs/{runId}`                       | One stored run with its record exactly as hashed.                                                                                                                                                                                                                                                                                                                                        |
| `GET tasks/{key}/evaluation-runs`                   | The task's stored runs, newest first; `version` narrows to one version.                                                                                                                                                                                                                                                                                                                  |
| `POST tasks/{key}/versions/{version}/qualification` | Qualifies a version (`QualifyTaskConfiguration`): configuration hash, the stored evaluation run's id and the caller's attestation id; needs `Idempotency-Key` and a signed change reason.                                                                                                                                                                                                |
| `POST tasks/{key}/versions/{version}/suspension`    | Suspends a version (`SuspendTaskConfiguration`); needs `Idempotency-Key` and a signed change reason.                                                                                                                                                                                                                                                                                     |
| `GET controls`                                      | The tenant's kill switches and budgets.                                                                                                                                                                                                                                                                                                                                                  |
| `PUT controls/{scope}`                              | Sets one control (`*` or a task key); needs `Idempotency-Key` and a signed change reason (`SetAgentControl`).                                                                                                                                                                                                                                                                            |
| `POST intent/route`                                 | Command-palette recommendations for a free-text query (rule based, no model).                                                                                                                                                                                                                                                                                                            |
| `POST biometrics/verify-liveness`                   | Issues a 15-minute scope-bound `liveness_token` (`tenant_id`, `issuer_id`, and optional `study_id` / `site_id`) after verifying PNG, JPEG, or WebP frame headers in temporary memory (`camera` requires `econsent_consent:capture` in scope or `subject` matching `subject_pseudonym`; `coordinator_fallback` requires `econsent_consent:capture` plus `study_id` and `site_id` access). |
| `POST mcp`                                          | Model Context Protocol (Streamable HTTP, stateless): the read tools as the signed-in caller, never wider.                                                                                                                                                                                                                                                                                |

## Configuration

Section `Intelligence` ([docs/ENVIRONMENT_VARIABLES.md](../../docs/ENVIRONMENT_VARIABLES.md)):

- `Anthropic:ApiKey` (or `ANTHROPIC_API_KEY`): no default; without it no Anthropic provider is registered.
- `Anthropic:Tiers:deep|balanced|fast`: the models behind `tier:` aliases (defaults `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5`).
- `FakeProvider`: registers the fake provider; refused outside `Development` and `Testing`.
- `RequestTimeoutSeconds`: the model call's timeout (default 120).
- `Prices:<model>:InputPerMillion|OutputPerMillion`: USD prices for the ledger's cost; without a price the cost is left empty.
- `Inference:*` and `Sandbox:AllowedClients`: providers, deployment mode, supplier approvals and the sandbox's extra clients ([Intelligence inference](../../docs/ENVIRONMENT_VARIABLES.md#intelligence-inference)). Outside Development an external provider, the Claude API included, is used only once its supplier is approved.

## Tests

- `tests/Cadence.UnitTests/Intelligence/Evaluation`: the set loader's refusals, each deterministic check, judge calibration, the harness floors and run records, comparisons, and the production monitor.
- `tests/Cadence.UnitTests/Intelligence`: domain (hash coverage, risk matrix, qualification gaps and usability), runner (qualification routing and refusals, kill switch, budget, timeout, failure, refused route, recorded route), providers, the inference router, outbound guard, sandbox, supplier records and the intent router. No test calls a model; the agent tools, citation verifier, Proposal writer, the MCP endpoint over `TestServer` and a scripted agent run.
- `tests/Cadence.ArchitectureTests/Architecture/InferenceSandboxBoundaryTests.cs`: no socket, hand-built `HttpClient`, process or file access in `Inference/` and `Sandbox/`.
- `tests/Cadence.ArchitectureTests/Architecture/IntelligenceToolBoundaryTests.cs`: `Tools/` and `Mcp/` cannot reach a write path.
- `tests/Cadence.IntegrationTests/Intelligence`: the module on PostgreSQL 18 through the kernel command pipeline: ledger hash and audit, kill switch in the runner and the command, budget, the recorded route and scrub counts, Row-Level Security between tenants, requester checks, qualification (unqualified, changed and suspended configurations refused; the qualifier's own signature, accepted evidence and a complete configuration required), and the application role's missing `DELETE`.
