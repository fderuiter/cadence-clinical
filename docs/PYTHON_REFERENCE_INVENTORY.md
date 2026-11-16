# Python Reference Inventory (#4789)

The Python files still in `packages/` are behavioural references for C# ports that do not exist yet or are not
yet covered by tests ([AGENTS.md](../AGENTS.md) pattern 11, [MIGRATION_WORKFLOW.md](MIGRATION_WORKFLOW.md)). They
are never built, run or imported by anything. Everything else was deleted once a tested C# port existed, the kernel
specification superseded it, or it had no behaviour worth keeping ([ADR-2492](adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md)). Deleted files stay recoverable from git history
(for example `git show 354f7ea:packages/database/graph.py`).

A file leaves this list when one of these is true:

- **Port missing:** a C# implementation lands with tests, or the kernel specification or an ADR records that the
  behaviour is not carried forward.
- **Port untested:** tests cover the C# port's main behaviour (the note says which paths are missing).
- **Ported, deletion pending:** the tested C# port exists; the file goes once a maintainer approves the deletion.

Delete the Python file, its row here and, when the folder has no Python left, its `__init__.py` files and
`pyproject.toml`, in the same PR. Update the package README's "Python reference sources" line too.

| Package | Port missing | Port untested | Ported, deletion pending |
| --- | ---: | ---: | ---: |
| [packages/ddf](#packagesddf) | 1 | 0 | 10 |
| [packages/engine_core](#packagesengine_core) | 5 | 0 | 17 |
| [packages/security](#packagessecurity) | 9 | 0 | 0 |

## packages/ddf

| Python file | What it does | C# today | Status | Notes |
| --- | --- | --- | --- | --- |
| `compiler.py` | 5-stage orchestrator; requires an accessibility validator port (fail-closed) | `UsdmCompiler.CompileUsdm`: reads the study, builds the IR with rules and item text, renders ODM and OpenRosa, runs the accessibility check fail-closed | Ported, deletion pending | ADR-2501, ADR-2502 |
| `models.py` | Pydantic IR, report, options and result models | `packages/ddf/src/Models.cs` (`DDFItemIR`, `DDFStudyIR`, `AccessibilityReport`, `CompileOptions`, `DDFCompilationResult`, `CompilerWarning`, `ItemLocalization`) | Ported, deletion pending | Covered through `DdfCompilerTests` and `DdfRulesTests` |
| `dataset_json_etl.py` | eCRF observations → SDTM Dataset-JSON | none | Port missing |  |
| `ports.py` | `AccessibilityValidatorPort` | `IAccessibilityValidator` (`packages/ddf/src/Accessibility.cs`) | Ported, deletion pending | `DdfCompilerTests` |
| `rules/ast.py` | DDF rule AST (skip logic, constraint, cross-form), node validation, `detect_circular_dependencies` | Replaced by the one rule language (K-RULE-3): item rules are `Cadence.Rules` expressions (`DdfItemRules`); cycles by `DdfDependencyGraph` | Ported, deletion pending | ADR-2502; `DdfRulesTests`. ODM paths (`field_ref` with form or visit, `indexed-repeat`) are not carried forward |
| `rules/compiler.py` | `sanitize_identifier`, `extract_appearance`, condition → XPath compiler | `DdfIdentifier.Sanitize` (deterministic, no random fallback), `RuleXPathCompiler` (safe literals), column width → appearance in `UsdmCompiler` | Ported, deletion pending | ADR-2502; `DdfRulesTests` |
| `smart_blocks.py` | Catalog of reusable form blocks (vital signs, AE log) and instantiation into a tripartite form | `CollectionSpecification` library items: content format and validator (`CollectionSpecificationJson`), the shipped vital signs block (`FormBlocks`) and instantiation into a protocol design `CollectionSpec` (`CollectionSpecificationInstantiator`), in `Cadence.Standards` | Ported, deletion pending | ADR-2503; `FormBlockTests`, `FormBlockPostgresTests`. Not carried forward: CDASH OIDs and SDTM domains (mapping specifications), widget layout, min/max ranges (rule-pack edit checks) and the AE log block |
| `stages/accessibility.py` | Runs validator; raises on WCAG or layout violations | `UsdmCompiler.CompileUsdm` throws `AccessibilityValidationException`; `XFormsAccessibilityValidator` is the built-in check | Ported, deletion pending | `DdfCompilerTests`, `XFormsAccessibilityValidatorTests` |
| `stages/ir_builder.py` | Builds IR: rules → XPath relevant/constraint, multi-language localisations, appearance | `UsdmCompiler.ParseUsdmToIr`: rules, appearance, and item `label`, `description`, `hint` and `localizations` (`ItemText`) | Ported, deletion pending | ADR-2502; `DdfRulesTests`. Guessing item text from USDM narrative content by name is not carried forward: the text comes from the item's own fields |
| `stages/normalizer.py` | USDM validation, fallback id/name synthesis with warnings, duplicate-id detection | `UsdmCompiler.ParseUsdmToIr`: a missing study id or name and an item without an id are filled in and reported as warnings; duplicate item ids and element names fail the compile | Ported, deletion pending | `DdfRulesTests`. Placeholder UUIDs for USDM `documentedBy` fields are not carried forward: nothing validates them against a schema |
| `tripartite.py` | Pydantic tripartite form definition (data, UI and logic schemas, migration matrix, UAT report) | Not carried forward ([ADR-2504](adr/2026-09-30-the-tripartite-form-definition-is-not-carried-forward.md)): data schema → `CollectionSpec` and `CollectionSpecification` library items; CDISC metadata → mapping specifications; constraints → rule packs; UI schema → DDF form design; migration matrix → Effectivity and Applicability; UAT report → evaluate-only simulation | Ported, deletion pending | Shapes only, no behaviour; nothing in C# read them |

## packages/engine_core

| Python file | What it does | C# today | Status | Notes |
| --- | --- | --- | --- | --- |
| `derived/engine.py` | Derived-variable math engine with unit normalisation and plausibility ranges | `DerivedVariableEngine` (`packages/engine_core/src/Derived/`), tested by `DerivedVariableEngineTests`; `CUSTOM_AST` formulas are `Cadence.Rules` expressions | Ported, deletion pending | ADR-2522; a missing or unknown sex is an error rather than female or male |
| `derived/models.py` | Formula, plausibility and result models | records in `packages/engine_core/src/Derived/DerivedModels.cs`; `ComputeChecksum` matches the Python checksum | Ported, deletion pending | ADR-2522 |
| `financial/contracts.py` | Tiered overhead, payee splits, line-item hash (Decimal) | `ContractCalculations` (`apps/payments/src/Domain/Ledger/`), tested by `ContractCalculationsTests` | Ported, deletion pending | ADR-2521 |
| `financial/engine.py` | Event-triggered payable engine | none: callers create a `PayableLedger` | Ported, deletion pending | Facade not carried forward (ADR-2521) |
| `financial/ledger.py` | Payable ledger with holdback reserve accounting | `PayableLedger` (`apps/payments/src/Domain/Ledger/`), tested by `PayableLedgerTests` | Ported, deletion pending | ADR-2521 |
| `financial/models.py` | Fee matrix, grant contract, payable models | records in `apps/payments/src/Domain/Ledger/GrantContractModels.cs` with kernel IDs; limits in `GrantContractValidator` | Ported, deletion pending | ADR-2521 |
| `fsm/engine.py` | Generic DAG state machine with Part 11 envelope | Superseded: lifecycles are state machines on kernel aggregates in `Cadence.Kernel.Domain` (spec §3), with authorization (`IAuthorizer`), reason for change and audit from the kernel command pipeline | Ported, deletion pending | No generic runtime state machine is carried forward |
| `fsm/models.py` | State and transition models, `Part11AuditEnvelope` | Superseded with `fsm/engine.py`; the kernel audit log records each command | Ported, deletion pending |  |
| `fsm/validator.py` | Graph validation (reachability, terminal states) | Superseded with `fsm/engine.py`; each aggregate's transitions are fixed in code and covered by its tests | Ported, deletion pending |  |
| `operations/engine.py` | Site startup greenlight facade | none | Port missing |  |
| `operations/evaluator.py` | Greenlight rules: IRB, contract, staff qualification, facility, IP depot | none | Port missing | Spec K-TIME-4/5 and K-AUTH-5 are the target |
| `operations/models.py` | Greenlight models | none | Port missing |  |
| `rules/ast.py` | Rule AST with `validate_node` shape validation (operand counts, required fields) | Not carried forward ([ADR-2506](adr/2026-09-30-the-engine-core-rule-ast-is-superseded-by-the-one-rule-language.md)): strict `RuleExpressionJson` parsing in the one rule language (K-RULE-3). The unused `Cadence.Core.Domain.Rules` engine goes with the approved deletion | Ported, deletion pending | `RuleExpressionTests` |
| `rules/sentinels.py` | Field-reference extraction, complexity, unresolved references, cycle detection | Not carried forward ([ADR-2506](adr/2026-09-30-the-engine-core-rule-ast-is-superseded-by-the-one-rule-language.md)): `RuleExpressionJson.MaxDepth`/`MaxNodes`; unknown items and skip-logic cycles in the DDF compile (ADR-2502); pack references to the `CollectionSpec` in #4728; derived-variable cycles in #5043 | Ported, deletion pending | `RuleExpressionTests`, `DdfRulesTests` |
| `sla/engine.py` | SLA countdown clocks with business hours and escalation tiers | `SlaEngine` (`packages/engine_core/src/Sla/`), tested by `SlaEngineTests`; the caller passes the instant a clock is read at | Ported, deletion pending | A schedule that can never run is refused (the Python looped forever) |
| `sla/models.py` | SLA models | records in `packages/engine_core/src/Sla/SlaModels.cs`; `SlaPolicy.ComputeChecksum` matches the Python checksum | Ported, deletion pending | Audit fields left to the store's GxP audit interceptor |
| `supply/engine.py` | `PredictiveSupplyEngine` facade (forecast, hierarchy, DTP, unblinding) | none: callers use the engines directly | Ported, deletion pending | Not carried forward (ADR-2520) |
| `supply/hierarchy.py` | Multi-echelon depot/site hierarchy roll-up and validation | `MultiEchelonHierarchyEngine` (`packages/engine_core/src/Supply/MultiEchelonHierarchyEngine.cs`), tested by `MultiEchelonHierarchyEngineTests` | Ported, deletion pending | #5046 |
| `supply/models.py` | Supply node, roll-up, forecast, DTP and unblinding models | `packages/engine_core/src/Supply/SupplyModels.cs`; obsolete unblinding records removed under ADR-2520 | Ported, deletion pending | Goes with `supply/engine.py`, `supply/hierarchy.py` and `supply/unblinding.py` |
| `supply/unblinding.py` | Emergency unblinding authorisation | none: Execution's `UNBLINDED` state change (PRD-SUB-005, #4982) is the only unblinding path | Ported, deletion pending | Not carried forward (ADR-2520); the kernel target is an Attestation-bearing event (spec Appendix A.1) |
| `timeline/engine.py` | Visit-window schedule and deviation assessment | `ExpectedSchedule` (kernel, [ADR-2487](adr/2026-09-30-expected-schedule-as-a-pure-function.md)) places timed activities with their windows and window status from the reference start (K-TIME-7) | Port missing | Early/late classification of an actual visit is `ExpectedSchedule.Assess` ([ADR-2507](adr/2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md)); major/minor is a Quality rule, not a fixed 7 days. Not in the kernel yet (#5045, ADR-2507): repeating cycle templates and visits anchored on another visit |
| `timeline/models.py` | Timeline models | none | Port missing |  |

## packages/security

| Python file | What it does | C# today | Status | Notes |
| --- | --- | --- | --- | --- |
| `auditor_token.py` | Issues and validates time-boxed read-only auditor JWTs signed with `GATEWAY_SECRET` (with a hard-coded fallback) | none (`AuditorWriteGuardMiddleware.cs` only enforces read-only access) | Port missing | Likely a Keycloak concern under ADR-2256, so the owner could decide to drop it |
| `cert_store.py` | X.509 register, revoke, CRL status, self-signed and trust checks | none | Port missing |  |
| `crypto_verifier.py` | `verify_electronic_signature`: JSON/PEM/XML signature parsing, XML-DSig stripping, mock-key detection, asymmetric verification or HMAC fallback | only the primitive `packages/security/src/AsymmetricSigner.AsymmetricVerify` | Port missing | The orchestration is not ported |
| `fail_fast.py` | Refuses to start in non-dev environments on missing or insecure-fallback secrets; brand validation | `GATEWAY_SECRET` (required, at least 32 chars), the execution demographics key and the gateway service URLs ([ADR-2490](adr/2026-09-30-fail-fast-startup-validation-of-required-settings.md)) | Port missing | See note S1 |
| `gating.py` | Which paths require e-signature step-up | Step-up is wired per endpoint: an endpoint that needs a signature adds `ElectronicSignatureEndpointFilter` ([ADR-2259](adr/2026-09-29-electronic-signature-step-up-re-authentication.md)), so no path list is needed | Port missing | Proposed for deletion as superseded |
| `permissions.py` | Second permission scheme: `PermissionEnum` (`study:read`, `rtsm:randomize`, …), `RoleEnum`, role → permission map | none (C# `PermissionMatrix` uses a different resource → actions scheme; none of the 26 colon permissions exist in C#) | Port missing | Ask the owner whether spec §9.3 capability tokens make it obsolete |
| `rbac.py` | Role aliases, role → resource → action matrix, `Principal`, site/study scope, payload masking, auditor checks | `packages/security/src/Rbac/RoleNormalization.cs`, `PermissionMatrix.cs`, `SiteAccessEvaluator.cs`, `StudyAccessEvaluator.cs`, `Blinding/FieldMasker.cs`, `Authentication/AuditorWriteGuardMiddleware.cs` | Port missing | See note S2 |
| `regulated_actions.py` | Semantic regulated-action registry and path detection rules | Superseded like `gating.py`: the signature meaning comes from the endpoint's filter (ADR-2259) | Port missing | Proposed for deletion with `gating.py` |
| `signing.py` | Gateway HMAC, canonical signature, asymmetric sign, Merkle root and block hash, inbound-email webhook HMAC with replay cache, site greenlight token | `Authentication/HmacSigner.cs`, `CanonicalSigner.cs`, `AsymmetricSigner.cs`, `Audit/AuditDigestEngine.cs` | Port missing | See note S3 |

**Note S1.** `fail_fast.py` refuses to start outside development when any required secret is missing or still
set to an insecure placeholder, and validates the brand settings. In C#, every service requires `GATEWAY_SECRET`
(at least 32 characters), execution requires a valid demographics encryption key and the gateway requires every
service URL outside Development (#4872, ADR-2490). To delete: cover the remaining services' required settings the
same way, or record that the Python's placeholder and brand checks are dropped.

**Note S2.** The C# `PermissionMatrix` has no entries for the roles `grants_manager`, `sponsor_clinical`,
`quality_manager`, `qa_lead` or `quality_oversight` (`rbac.py:645-763`). The `system` and `anonymous` entries
are rightly dropped (K-TX-5). The rest is ported and tested. To delete: port those five role bundles or record
that they are dropped.

**Note S3.** Not ported:
- `verify_inbound_email_signature` (eTMF inbound webhook; `INBOUND_EMAIL_HMAC_SECRET`).
- `generate_greenlight_token` / `verify_greenlight_token`.
- `compute_merkle_root` / `compute_block_hash`.
