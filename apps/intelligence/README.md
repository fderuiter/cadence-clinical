# Intelligence module

The Intelligence engine's runtime for AI agents ([ADR-2630](../../docs/adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md), #5374). It is a kernel module ([ADR-2600](../../docs/adr/index.md)) composed into the kernel host [`apps/kernel-api`](../kernel-api/README.md), not a service of its own; it replaces the retired `apps/ai_gateway` ([ADR-2610](../../docs/adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)).

**Status:** foundation. The module calls a model, records every call in an append-only ledger, and enforces per-tenant kill switches and token budgets. Agents do not yet act on clinical records: when they do, they act only through kernel Proposals (K-AI-1..8). Versioned, qualified task configurations come with #5385; PHI scrubbing, data residency, more providers and a local-only mode with #5550; an agent actor with requester-scoped grants with #4783.

## Project

- Project: `apps/intelligence/src/Cadence.Modules.Intelligence.csproj` (namespace `Cadence.Modules.Intelligence`). It references `Cadence.Kernel.Abstractions` and the module SDK `Cadence.Kernel.Modules` only (K-XS-2).
- `IntelligenceModule`: `AddCadenceIntelligenceModule(configuration, environment, connectionString)` registers the module, its command store, migrations, tenant-scoped read context, handlers, providers and the `AgentRunner`; `AddAgentTask(TaskConfiguration)` registers a task; `MapIntelligenceModule()` maps the endpoints.
- `Domain/`: `TaskConfiguration` (key, version, provider, model or `tier:` alias, instructions template, output limit, autonomy level, and a SHA-256 `ContentHash` over all of them, K-AI-5), `PromptTemplate` (strict `{{name}}` rendering), `AgentRun` (one ledger entry with its own content hash), `AgentControl` (a kill switch and monthly token budget for all tasks, `*`, or one task key), `IntelligenceErrors`.
- `Application/`: the module commands `StartAgentRun`, `CompleteAgentRun` (capability `agent_run.record`, verb Propose) and `SetAgentControl` (capability `agent_control.set`, verb Execute) with their handlers and validators; the ports `ITaskConfigurationCatalog`, `IModelProvider`, `IModelProviderRegistry`, `IAgentLedger`; and `AgentRunner`.
- `Infrastructure/Models/`: the model port is Microsoft.Extensions.AI `IChatClient`. The Anthropic provider uses the official `Anthropic` SDK and is configured only when an API key is set; `FakeChatClient` answers deterministically and is allowed only in `Development` and `Testing`, so CI never calls a real model.
- `Infrastructure/Persistence/`: `IntelligenceDbContext` (schema `intelligence`, tables `agent_runs` and `agent_controls`, both under tenant Row-Level Security), its migrations, `EfAgentLedger` and `IntelligenceDatabase.GrantAppRoleSql`.
- `Intent/` and `Endpoints/IntentEndpoints.cs`: the rule-based command-palette intent router, moved from the AI gateway unchanged.

## How a run works

`AgentRunner.RunAsync` resolves the task's configuration, provider and model, refuses the run when a control is off or the tenant's monthly tokens are spent, renders the instructions, and records `StartAgentRun` through the kernel command pipeline before it calls the model. The model call has a timeout (`Intelligence:RequestTimeoutSeconds`); success, failure and cancellation are all recorded with `CompleteAgentRun`, with token counts and cost, and raise `intelligence.AgentRunCompleted`. The start command re-checks the kill switch inside its transaction. Logs carry ids and counts only, never prompts or answers.

Every ledger entry holds the task version and configuration hash, the resolved model, the requester, the request and the answer. It is written only through the pipeline (audit columns, idempotency, authorization) and the application role cannot delete it. Only the person who started a run can complete it or read it.

## HTTP surface

All routes need the gateway-signed caller and run in the caller's kernel tenant. The wire is snake_case.

| Route (`/api/v1/ai/…`) | Answers                                                                                                       |
| ---------------------- | ------------------------------------------------------------------------------------------------------------- |
| `GET runs`             | The caller's own runs, newest first, each with `content_hash_verified`.                                       |
| `GET runs/{runId}`     | One of the caller's runs; another person's run is 404.                                                        |
| `GET tasks`            | The registered task configurations with their versions and hashes.                                            |
| `GET controls`         | The tenant's kill switches and budgets.                                                                       |
| `PUT controls/{scope}` | Sets one control (`*` or a task key); needs `Idempotency-Key` and a signed change reason (`SetAgentControl`). |
| `POST intent/route`    | Command-palette recommendations for a free-text query (rule based, no model).                                 |

## Configuration

Section `Intelligence` ([docs/ENVIRONMENT_VARIABLES.md](../../docs/ENVIRONMENT_VARIABLES.md)):

- `Anthropic:ApiKey` (or `ANTHROPIC_API_KEY`): no default; without it no Anthropic provider is registered.
- `Anthropic:Tiers:deep|balanced|fast`: the models behind `tier:` aliases (defaults `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5`).
- `FakeProvider`: registers the fake provider; refused outside `Development` and `Testing`.
- `RequestTimeoutSeconds`: the model call's timeout (default 120).
- `Prices:<model>:InputPerMillion|OutputPerMillion`: USD prices for the ledger's cost; without a price the cost is left empty.

## Tests

- `tests/Cadence.UnitTests/Intelligence`: domain, runner (kill switch, budget, timeout, failure), providers and the intent router.
- `tests/Cadence.IntegrationTests/Intelligence`: the module on PostgreSQL 18 through the kernel command pipeline: ledger hash and audit, kill switch in the runner and the command, budget, Row-Level Security between tenants, requester checks, and the application role's missing `DELETE`.
