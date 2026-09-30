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
| [packages/ddf](#packagesddf) | 6 | 0 | 4 |
| [packages/engine_core](#packagesengine_core) | 21 | 0 | 0 |
| [packages/security](#packagessecurity) | 9 | 0 | 0 |
| [packages/storage](#packagesstorage) | 2 | 0 | 4 |

## packages/ddf

| Python file | What it does | C# today | Status | Notes |
| --- | --- | --- | --- | --- |
| `compiler.py` | 5-stage orchestrator; requires an accessibility validator port (fail-closed) | `packages/ddf/src/UsdmCompiler.cs` (runs the accessibility stage fail-closed; has no normalizer, IR builder or rule stages) | Port missing | Goes with `stages/normalizer.py` and `stages/ir_builder.py` |
| `dataset_json_etl.py` | eCRF observations → SDTM Dataset-JSON | none | Port missing |  |
| `ports.py` | `AccessibilityValidatorPort` | `IAccessibilityValidator` (`packages/ddf/src/Accessibility.cs`) | Ported, deletion pending | `DdfCompilerTests` |
| `rules/ast.py` | DDF rule AST (skip logic, constraint, cross-form), node validation, `detect_circular_dependencies` | Replaced by the one rule language (K-RULE-3): item rules are `Cadence.Rules` expressions (`DdfItemRules`); cycles by `DdfDependencyGraph` | Ported, deletion pending | ADR-2502; `DdfRulesTests`. ODM paths (`field_ref` with form or visit, `indexed-repeat`) are not carried forward |
| `rules/compiler.py` | `sanitize_identifier`, `extract_appearance`, condition → XPath compiler | `DdfIdentifier.Sanitize` (deterministic, no random fallback), `RuleXPathCompiler` (safe literals), column width → appearance in `UsdmCompiler` | Ported, deletion pending | ADR-2502; `DdfRulesTests` |
| `smart_blocks.py` | Catalog of reusable form blocks (vitals, hematology, ConMeds, AE) and instantiation | none | Port missing |  |
| `stages/accessibility.py` | Runs validator; raises on WCAG or layout violations | `UsdmCompiler.CompileUsdm` throws `AccessibilityValidationException`; `XFormsAccessibilityValidator` is the built-in check | Ported, deletion pending | `DdfCompilerTests`, `XFormsAccessibilityValidatorTests` |
| `stages/ir_builder.py` | Builds IR: rules → XPath relevant/constraint, multi-language localisations, appearance | `UsdmCompiler.ParseUsdmToIr` compiles rules and appearance (ADR-2502) | Port missing | Multi-language labels, hints and descriptions from USDM narrative content are not ported |
| `stages/normalizer.py` | USDM validation, fallback id/name synthesis with warnings, duplicate-id detection | none | Port missing |  |
| `tripartite.py` | Pydantic tripartite form definition (data, UI and logic schemas, migration matrix, UAT report) | none | Port missing | Disposition against spec CollectionSpec |

## packages/engine_core

| Python file | What it does | C# today | Status | Notes |
| --- | --- | --- | --- | --- |
| `derived/engine.py` | Derived-variable math engine with unit normalisation and plausibility ranges | none | Port missing |  |
| `derived/models.py` | Formula, plausibility and result models | none | Port missing |  |
| `financial/contracts.py` | Tiered overhead, payee splits, line-item hash (Decimal) | none (`apps/payments/src/Domain/PaymentEntities.cs` holds entities only; `tests/Cadence.UnitTests/Payments/PaymentDecimalCalculationTests.cs` computes inline) | Port missing |  |
| `financial/engine.py` | Event-triggered payable engine | none | Port missing |  |
| `financial/ledger.py` | Payable ledger with holdback reserve accounting | none | Port missing |  |
| `financial/models.py` | Fee matrix, grant contract, payable models | entity shapes only in `apps/payments` | Port missing |  |
| `fsm/engine.py` | Generic DAG state machine with Part 11 envelope | none | Port missing |  |
| `fsm/models.py` | State and transition models, `Part11AuditEnvelope` | none | Port missing |  |
| `fsm/validator.py` | Graph validation (reachability, terminal states) | none | Port missing |  |
| `operations/engine.py` | Site startup greenlight facade | none | Port missing |  |
| `operations/evaluator.py` | Greenlight rules: IRB, contract, staff qualification, facility, IP depot | none | Port missing | Spec K-TIME-4/5 and K-AUTH-5 are the target |
| `operations/models.py` | Greenlight models | none | Port missing |  |
| `rules/ast.py` | Rule AST with `validate_node` shape validation (operand counts, required fields) | `packages/engine_core/src/Rules/RuleAst.cs` (plain classes, no validation) | Port missing | Node validation not ported |
| `rules/sentinels.py` | Field-reference extraction, complexity, unresolved references, cycle detection | none | Port missing |  |
| `sla/engine.py` | SLA countdown clocks with business hours and escalation tiers | none | Port missing |  |
| `sla/models.py` | SLA models | none | Port missing |  |
| `supply/engine.py` | `PredictiveSupplyEngine` facade (forecast, hierarchy, DTP, unblinding) | none | Port missing |  |
| `supply/hierarchy.py` | Multi-echelon depot/site hierarchy roll-up and validation | models only in `SupplyModels.cs` | Port missing |  |
| `supply/unblinding.py` | Emergency unblinding authorisation | models only in `SupplyModels.cs` | Port missing | Spec Appendix A.1 (Attestation-bearing event) is the target |
| `timeline/engine.py` | Visit-window schedule and deviation assessment | none | Port missing | Spec K-TIME-7 is the target |
| `timeline/models.py` | Timeline models | none | Port missing |  |

## packages/security

| Python file | What it does | C# today | Status | Notes |
| --- | --- | --- | --- | --- |
| `auditor_token.py` | Issues and validates time-boxed read-only auditor JWTs signed with `GATEWAY_SECRET` (with a hard-coded fallback) | none (`AuditorWriteGuardMiddleware.cs` only enforces read-only access) | Port missing | Likely a Keycloak concern under ADR-2256, so the owner could decide to drop it |
| `cert_store.py` | X.509 register, revoke, CRL status, self-signed and trust checks | none | Port missing |  |
| `crypto_verifier.py` | `verify_electronic_signature`: JSON/PEM/XML signature parsing, XML-DSig stripping, mock-key detection, asymmetric verification or HMAC fallback | only the primitive `packages/security/src/AsymmetricSigner.AsymmetricVerify` | Port missing | The orchestration is not ported |
| `fail_fast.py` | Refuses to start in non-dev environments on missing or insecure-fallback secrets; brand validation | `GATEWAY_SECRET` (required, at least 32 chars), the execution demographics key and the gateway service URLs ([ADR-2490](adr/2026-09-30-fail-fast-startup-validation-of-required-settings.md)) | Port missing | See note S1 |
| `gating.py` | Which paths require e-signature step-up | none | Port missing | ADR-2256 §5 says step-up is not wired yet (#4761) |
| `permissions.py` | Second permission scheme: `PermissionEnum` (`study:read`, `rtsm:randomize`, …), `RoleEnum`, role → permission map | none (C# `PermissionMatrix` uses a different resource → actions scheme; none of the 26 colon permissions exist in C#) | Port missing | Ask the owner whether spec §9.3 capability tokens make it obsolete |
| `rbac.py` | Role aliases, role → resource → action matrix, `Principal`, site/study scope, payload masking, auditor checks | `packages/security/src/Rbac/RoleNormalization.cs`, `PermissionMatrix.cs`, `SiteAccessEvaluator.cs`, `StudyAccessEvaluator.cs`, `Blinding/FieldMasker.cs`, `Authentication/AuditorWriteGuardMiddleware.cs` | Port missing | See note S2 |
| `regulated_actions.py` | Semantic regulated-action registry and path detection rules | none | Port missing | Goes with `gating.py` / #4761 |
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

## packages/storage

| Python file | What it does | C# today | Status | Notes |
| --- | --- | --- | --- | --- |
| `adapters/minio_adapter.py` | MinIO subclass of the S3 adapter | `S3BlobStore` with `Storage:ServiceUrl` (path-style addressing) | Ported, deletion pending | `S3BlobStoreTests`, `StorageRegistrationTests` |
| `adapters/s3_adapter.py` | S3 put/get with SHA-256 check, SSE, presigned get/put URLs, multipart | `packages/storage/src/S3BlobStore.cs` | Ported, deletion pending | `S3BlobStoreTests` |
| `factory.py` | Picks the local, S3 or MinIO adapter from environment | `AddCadenceStorage` (`StorageServiceCollectionExtensions.cs`); the provider is named, never guessed from the endpoint | Ported, deletion pending | `StorageRegistrationTests` |
| `gdrive.py` | Google Drive run-manifest upload for GxP recordings | none (`pnpm cadence record setup-gdrive` prints "not implemented yet", `packages/cli/src/Commands/RecordCommands.cs:10`) | Port missing |  |
| `local_store.py` | Filesystem provider with checksum and path-traversal guard | none | Port missing |  |
| `s3_store.py` | Older S3 provider | `S3BlobStore` (a duplicate of `adapters/s3_adapter.py`) | Ported, deletion pending | `S3BlobStoreTests` |
