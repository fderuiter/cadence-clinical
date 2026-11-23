---
title: "Clinical Kernel v0.2: One Canonical Body of Clinical Truth"
date: "2026-09-29"
authors:
  - "@fderuiter"
summary: "Why Cadence inverted the multi-product eClinical stack around one PostgreSQL clinical kernel, six domain engines, bitemporal evidence, and native ProtocolVersion snapshots."
tags:
  - "kernel"
  - "architecture"
  - "postgresql"
  - "gxp"
related:
  - text: "Clinical Kernel Specification v0.2"
    link: "../kernel/CLINICAL_KERNEL_SPECIFICATION.md"
  - text: "ADR-2244: Clinical Kernel Modular Monolith Architecture"
    link: "../adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md"
  - text: "ADR-2245: Native PostgreSQL ProtocolVersion Snapshots & Neo4j Retirement"
    link: "../adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md"
draft: false
prev: false
next: false
---

# Clinical Kernel v0.2: One Canonical Body of Clinical Truth

Conventional eClinical suites assemble separate products—EDC, CTMS, eTMF, RTSM, safety intake, and a standards validator—each maintaining its own definition of a study, a site, and a participant. Synchronizing those copies across message queues and batch exports introduces drift precisely where regulatory scrutiny is highest. With [Clinical Kernel Specification v0.2](../kernel/CLINICAL_KERNEL_SPECIFICATION.md), we inverted that topology so every application builds on one canonical model of clinical evidence.

## The Founding Invariant (`K-TRUTH-1`)

At the center of v0.2 is **K-TRUTH-1**: _no product owns clinical truth, and no representation is clinical truth_. An eCRF screen, an SDTM dataset, an ADaM analysis table, a FHIR resource, and a rendered protocol PDF are projections over canonical facts keyed by versioned clinical concepts.

Two immediate architectural rules follow from that invariant:

1. **Scope discipline (`K-SCOPE-1`):** A concept belongs in the kernel only when it explains the provenance, authority, meaning, or lifecycle of clinical evidence across multiple applications. Product-specific workflow chrome stays in modules.
2. **Forbidden redefinitions (`K-XS-1`):** Application modules never declare competing `Study`, `Site`, `Participant`, `Observation`, `Role`, or `Delegation` entities, enforced continuously by ArchUnitNET tests.

## Six Engines Over One Transaction Boundary

Rather than splitting clinical concepts into chatty network microservices, [ADR-2244](../adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) organizes the domain into six engines—**Knowledge**, **Execution**, **Evidence**, **Trust**, **Quality**, and **Intelligence**—sharing one PostgreSQL schema and one ACID command pipeline:

- **Bitemporal and append-only corrections:** Contestable facts track both valid time and recorded time through `IClock` (`K-TIME-3`), while observation corrections append immutable revisions with an audited reason for change (`K-FACT-1`).
- **Evidence-backed authority:** Command capabilities are evaluated from active delegations, training qualifications, and study-site effectivity rather than static role strings.
- **Deterministic lineage:** Every derived projection records the exact inputs and mapping specification version that produced it.

## Retiring Neo4j for PostgreSQL `ProtocolVersion` Snapshots

Earlier iterations stored CDISC USDM protocol graphs in Neo4j while recording trial execution in PostgreSQL. Maintaining distributed consistency across two database engines created unnecessary operational and 21 CFR Part 11 risk because an approved clinical protocol version is immutable once published.

Under [ADR-2245](../adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md), attested `ProtocolVersion` trees live directly in PostgreSQL alongside enrollment, expected visit schedules, and observations. Retiring Neo4j eliminated dual-write failure modes, simplified local onboarding and CI containers to PostgreSQL 18, and bound every captured observation transactionally to the protocol version in force.
