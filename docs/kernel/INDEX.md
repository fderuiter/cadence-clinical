# Clinical Kernel & Roadmap Index

Normative Clinical Kernel v0.2 specification, rule packs, indicators, mapping specs, and clock migration. Part of the sharded [Documentation Index](../DOCUMENTATION_INDEX.md); rows in each table are sorted alphabetically by file path.

## Kernel and roadmap

| Document | Status | What it is |
| --- | --- | --- |
| [Kernel decisions K001–K032](../adr/index.md) | Current | Kernel decisions and their ADRs; [ARCHITECTURE §2](../../ARCHITECTURE.md#2-key-decisions) summarises them with the platform decisions |
| [Kernel specification v0.2](CLINICAL_KERNEL_SPECIFICATION.md) | Normative | The model every capability builds on |
| [Roadmap (spec §15)](CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap) | Normative | Phases 0–5, tracked in epic [#4719](https://github.com/fderuiter/cadence-clinical/issues/4719) |
| [Clock migration backlog](CLOCK_MIGRATION_BACKLOG.md) | Current | Remaining `DateTime.Now/UtcNow` call sites to move to `IClock` (K-TIME-3) |
| [Kernel package guide](https://github.com/fderuiter/cadence-clinical/blob/main/packages/kernel/README.md) | Current | How the kernel projects are built and used: command pipeline, tenancy, NodeRef, explanations, authorization dry runs, search, protocol versions, Concept and Standards Registries, rule packs, validation findings, activity stream, expected schedule, IdentityVault, grants and qualifications |
| [Indicators](INDICATORS.md) | Current | Writing, releasing and evaluating Study Pulse indicators, KRIs and QTLs (spec §10.4, ADR-2488) |
| [Mapping specifications](MAPPING_SPECIFICATIONS.md) | Current | Writing, pinning and applying mapping specifications; K-STD-1 (spec §6.3, ADR-2463) |
| [Rule packs](RULE_PACKS.md) | Current | Writing, releasing and benchmarking rule packs (spec §10.1, ADR-2461) |
