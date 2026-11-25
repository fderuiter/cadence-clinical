# Python Reference Inventory (#4789)

> **Status: Historical (Completed — 2026-11-24).** Zero Python reference files remain in `apps/` or `packages/`, and `PathPatternValidatorService` rejects new `.py` files under those trees.

During the September–November 2026 Python-to-.NET 10 migration ([MIGRATION_WORKFLOW.md](MIGRATION_WORKFLOW.md)), this inventory tracked legacy Python files retained temporarily in `packages/` while their C# ports, tests, or architectural dispositions were completed under [ADR-2492](adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md).

All remaining Python files in `packages/engine_core/timeline/` (`engine.py`, `models.py`) and `packages/security/` (`cert_store.py`, `crypto_verifier.py`, `fail_fast.py`, `rbac.py`, `signing.py`) were retired in [ADR-2509](adr/2026-11-24-retire-remaining-python-references-and-scanner-residue.md), which records the superseding `.NET 10` implementations (`ExpectedSchedule`, `SigTokenVerifier`, `NotificationBrandingOptions`, `RoleNormalization`, `PermissionMatrix`, `StartupGreenlightEndpoints`, and `GxPAuditInterceptor`) and deferred module items ([ADR-2507](adr/2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md), [ADR-2610](adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)). Earlier package dispositions are recorded in [ADR-2504](adr/2026-09-30-the-tripartite-form-definition-is-not-carried-forward.md), [ADR-2505](adr/2026-09-30-recording-evidence-goes-to-controlled-object-storage.md), [ADR-2506](adr/2026-09-30-the-engine-core-rule-ast-is-superseded-by-the-one-rule-language.md), [ADR-2520](adr/2026-09-30-supply-unblinding-and-facade-not-carried-forward.md), [ADR-2521](adr/2026-09-30-grant-payables-ledger-in-decimal-with-kernel-ids.md), and [ADR-2522](adr/2026-09-30-derived-variables-ported-as-fixed-standard-formulas.md). Deleted sources remain recoverable from git history.

## Active Inventory

| Package | Port missing | Port untested | Ported, deletion pending |
| ------- | -----------: | ------------: | -----------------------: |

_Zero active Python reference files remain across the repository._
